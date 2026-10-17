using System.Collections.Concurrent;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Expovice.Api.Options;
using Expovice.Contracts;
using Microsoft.Extensions.Options;

namespace Expovice.Api.Services;

/// <summary>Stores user photos and hands out short-lived read URLs.</summary>
public interface IPhotoStore
{
    /// <summary>Saves the photo and returns its blob name (what the database stores).</summary>
    Task<string> SaveAsync(Guid userId, PhotoSlot slot, Stream content, string contentType, CancellationToken ct);

    Task DeleteAsync(PhotoSlot slot, string blobName, CancellationToken ct);

    /// <summary>A time-limited read URL, or null for no photo. ID documents never get a URL.</summary>
    Task<string?> GetReadUrlAsync(string? blobName, CancellationToken ct);
}

public static class PhotoTypes
{
    private static ReadOnlySpan<byte> PngMagic => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static readonly IReadOnlyDictionary<string, string> Extensions = new Dictionary<string, string>
    {
        ["image/jpeg"] = "jpg",
        ["image/png"] = "png",
        ["image/webp"] = "webp",
    };

    /// <summary>Checks the file's magic bytes, so a renamed non-image is rejected.</summary>
    public static bool HasImageSignature(ReadOnlySpan<byte> h) =>
        (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) // JPEG
        || (h.Length >= 8 && h[..8].SequenceEqual(PngMagic)) // PNG
        || (h.Length >= 12 && h[..4].SequenceEqual("RIFF"u8) && h[8..12].SequenceEqual("WEBP"u8)); // WebP

    public static string BlobName(Guid userId, PhotoSlot slot, string contentType) =>
        $"{userId}/{slot.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}.{Extensions[contentType]}";
}

/// <summary>
/// Azure Blob Storage implementation. Singleton and thread-safe: BlobServiceClient is thread-safe and the
/// user-delegation key cache is guarded by a SemaphoreSlim.
/// </summary>
public sealed class BlobPhotoStore : IPhotoStore, IDisposable
{
    private readonly BlobServiceClient _service;
    private readonly StorageOptions _options;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _keyLock = new(1, 1);
    private readonly ConcurrentDictionary<string, bool> _containersReady = new();
    private UserDelegationKey? _delegationKey;

    public BlobPhotoStore(IOptions<StorageOptions> options, TimeProvider time)
    {
        _options = options.Value;
        _time = time;
        _service = !string.IsNullOrWhiteSpace(_options.ConnectionString)
            ? new BlobServiceClient(_options.ConnectionString)
            : new BlobServiceClient(
                _options.BlobServiceUri ?? throw new InvalidOperationException("Storage:BlobServiceUri or Storage:ConnectionString is required."),
                new DefaultAzureCredential());
    }

    public async Task<string> SaveAsync(Guid userId, PhotoSlot slot, Stream content, string contentType, CancellationToken ct)
    {
        var name = PhotoTypes.BlobName(userId, slot, contentType);
        var container = await ContainerAsync(slot, ct);
        await container.GetBlobClient(name).UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            ct);
        return name;
    }

    public async Task DeleteAsync(PhotoSlot slot, string blobName, CancellationToken ct)
    {
        var container = await ContainerAsync(slot, ct);
        await container.DeleteBlobIfExistsAsync(blobName, cancellationToken: ct);
    }

    public async Task<string?> GetReadUrlAsync(string? blobName, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(blobName))
        {
            return null;
        }

        var blob = _service.GetBlobContainerClient(_options.PhotosContainer).GetBlobClient(blobName);
        var sas = new BlobSasBuilder(BlobSasPermissions.Read, _time.GetUtcNow().AddMinutes(_options.ReadUrlMinutes))
        {
            BlobContainerName = blob.BlobContainerName,
            BlobName = blob.Name,
            Resource = "b",
        };

        // Shared-key clients (Azurite / connection string) can sign directly; managed identity needs a delegation key.
        if (blob.CanGenerateSasUri)
        {
            return blob.GenerateSasUri(sas).ToString();
        }

        var key = await GetDelegationKeyAsync(ct);
        var uri = new BlobUriBuilder(blob.Uri) { Sas = sas.ToSasQueryParameters(key, _service.AccountName) };
        return uri.ToUri().ToString();
    }

    private async Task<UserDelegationKey> GetDelegationKeyAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        // Refresh when fewer than (ReadUrlMinutes + 5) minutes remain, so every SAS we sign outlives its URL lifetime.
        var minRemaining = TimeSpan.FromMinutes(_options.ReadUrlMinutes + 5);
        var current = _delegationKey;
        if (current is not null && current.SignedExpiresOn - now > minRemaining)
        {
            return current;
        }

        await _keyLock.WaitAsync(ct);
        try
        {
            if (_delegationKey is null || _delegationKey.SignedExpiresOn - now <= minRemaining)
            {
                var response = await _service.GetUserDelegationKeyAsync(now.AddMinutes(-5), now.AddHours(6), ct);
                _delegationKey = response.Value;
            }
            return _delegationKey;
        }
        finally
        {
            _keyLock.Release();
        }
    }

    private async Task<BlobContainerClient> ContainerAsync(PhotoSlot slot, CancellationToken ct)
    {
        var name = slot == PhotoSlot.IdFront ? _options.IdDocumentsContainer : _options.PhotosContainer;
        var container = _service.GetBlobContainerClient(name);
        if (!_containersReady.ContainsKey(name))
        {
            await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
            _containersReady[name] = true;
        }
        return container;
    }

    public void Dispose() => _keyLock.Dispose();
}

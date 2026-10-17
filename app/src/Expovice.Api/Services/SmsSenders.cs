using Azure.Communication.Sms;
using Expovice.Api.Infrastructure;
using Expovice.Api.Options;
using Microsoft.Extensions.Options;

namespace Expovice.Api.Services;

/// <summary>Delivers a text message. Swap implementations (ACS, MSG91, ...) without touching OTP logic.</summary>
public interface ISmsSender
{
    Task SendAsync(string phoneNumber, string message, CancellationToken ct);
}

/// <summary>DEV ONLY: writes the message to the log instead of sending it. Used when no SMS provider is configured.</summary>
public sealed class LoggingSmsSender(ILogger<LoggingSmsSender> logger) : ISmsSender
{
    public Task SendAsync(string phoneNumber, string message, CancellationToken ct)
    {
        logger.LogWarning("DEV-ONLY SMS (no SMS provider configured) to {Phone}: {Message}", PhoneMask.Mask(phoneNumber), message);
        return Task.CompletedTask;
    }
}

/// <summary>Sends via Azure Communication Services. SmsClient is thread-safe, so this is a singleton.</summary>
public sealed class AzureCommunicationSmsSender(
    IOptions<SmsOptions> options,
    ILogger<AzureCommunicationSmsSender> logger) : ISmsSender
{
    private readonly SmsClient _client = new(options.Value.ConnectionString);
    private readonly string _from = options.Value.From ?? throw new InvalidOperationException("Sms:From is required when Sms:ConnectionString is set.");

    public async Task SendAsync(string phoneNumber, string message, CancellationToken ct)
    {
        var result = await _client.SendAsync(_from, phoneNumber, message, cancellationToken: ct);
        if (!result.Value.Successful)
        {
            logger.LogError("SMS to {Phone} failed: {Status} {Error}", PhoneMask.Mask(phoneNumber), result.Value.HttpStatusCode, result.Value.ErrorMessage);
            throw new InvalidOperationException("SMS delivery failed.");
        }
    }
}

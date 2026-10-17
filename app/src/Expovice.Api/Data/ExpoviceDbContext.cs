using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Expovice.Api.Data;

/// <summary>
/// Unit of work for one HTTP request. Registered as scoped: a DbContext is NOT thread-safe,
/// so never share an instance across concurrent tasks.
/// </summary>
public sealed class ExpoviceDbContext(DbContextOptions<ExpoviceDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<ProviderProfile> Providers => Set<ProviderProfile>();
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums as readable strings, so the DB stays understandable and reordering an enum is safe.
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<string>().HaveMaxLength(256);

        // SQLite (tests) can't ORDER BY DateTimeOffset; store it as a sortable number there. SQL Server is unaffected.
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.PhoneNumber).IsUnique().HasFilter("[PhoneNumber] IS NOT NULL");
            e.HasIndex(u => new { u.ExternalProvider, u.ExternalSubject })
                .IsUnique()
                .HasFilter("[ExternalSubject] IS NOT NULL");
            e.Property(u => u.PhoneNumber).HasMaxLength(16);
            e.Property(u => u.HomeLine).HasMaxLength(Contracts.Limits.AddressMaxLength);
            e.HasOne(u => u.Provider).WithOne(p => p.User).HasForeignKey<ProviderProfile>(p => p.UserId);
        });

        modelBuilder.Entity<ProviderProfile>(e =>
        {
            e.HasKey(p => p.UserId);
            e.HasIndex(p => new { p.Latitude, p.Longitude });
            e.Property(p => p.Bio).HasMaxLength(Contracts.Limits.BioMaxLength);
            // Primitive collections (List<string>, List<DayOfWeek>) map to JSON columns automatically.
            e.Ignore(p => p.Rating);
            e.Ignore(p => p.WorkPhotoBlobs);
        });

        modelBuilder.Entity<ServiceRequest>(e =>
        {
            e.HasIndex(r => new { r.ProviderId, r.Status });
            e.HasIndex(r => new { r.CustomerId, r.CreatedAt });
            e.Property(r => r.Note).HasMaxLength(Contracts.Limits.NoteMaxLength);
            e.HasOne(r => r.Customer).WithMany().HasForeignKey(r => r.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.Provider).WithMany().HasForeignKey(r => r.ProviderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OtpChallenge>(e =>
        {
            e.HasKey(o => o.PhoneNumber);
            e.Property(o => o.PhoneNumber).HasMaxLength(16);
            e.Property(o => o.CodeHash).HasMaxLength(32);
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.UserId);
            e.Property(t => t.TokenHash).HasMaxLength(32);
        });
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PortfolioManager.Api.Models;

namespace PortfolioManager.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Price> Prices => Set<Price>();
    public DbSet<User> Users => Set<User>();

    // SQLite has no timezone-aware type, so store every DateTime as UTC and
    // mark it as UTC when reading it back.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    private class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Asset>(e =>
        {
            e.Property(a => a.Symbol).HasMaxLength(20).IsRequired();
            e.Property(a => a.Identifier).HasMaxLength(100).IsRequired();
            e.Property(a => a.Name).HasMaxLength(100).IsRequired();
            e.Property(a => a.Type).HasMaxLength(50);
            e.Property(a => a.TsetmcInsCode).HasMaxLength(30);
            e.HasIndex(a => a.Symbol).IsUnique();
            e.HasIndex(a => a.Identifier).IsUnique();
        });

        modelBuilder.Entity<Price>(e =>
        {
            e.HasOne(p => p.Asset)
                .WithMany(a => a.Prices)
                .HasForeignKey(p => p.AssetId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.AssetId, p.Date });
        });

        modelBuilder.Entity<User>(e =>
        {
            e.Property(u => u.Username).HasMaxLength(50).IsRequired().UseCollation("NOCASE");
            e.Property(u => u.PasswordHash).IsRequired();
            e.Property(u => u.Role).HasMaxLength(20).IsRequired();
            e.Property(u => u.SecurityStamp).HasMaxLength(64).IsRequired();
            e.HasIndex(u => u.Username).IsUnique();
        });
    }
}

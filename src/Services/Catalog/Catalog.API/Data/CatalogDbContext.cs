using BuildingBlocks.Messaging;
using Catalog.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Catalog.API.Data;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public const string ConnectionName = "catalogdb";

    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(product =>
        {
            product.ToTable("products");
            product.HasKey(p => p.Id);
            product.Property(p => p.Name).HasMaxLength(100).IsRequired();
            product.Property(p => p.Description).HasMaxLength(1000);
            product.Property(p => p.Category).HasMaxLength(50).IsRequired();
            product.Property(p => p.Price).HasPrecision(18, 2);
            product.Property(p => p.ImageUrl).HasMaxLength(500);
            product.HasIndex(p => p.Category);
        });

        // Integration events are staged here in the same transaction as product changes.
        modelBuilder.AddOutbox();
    }

    /// <summary>Inserts the demo products into an empty database. Runs as part of Migrate().</summary>
    internal static async Task SeedAsync(DbContext db, CancellationToken ct)
    {
        var products = db.Set<Product>();
        if (await products.AnyAsync(ct)) return;

        products.AddRange(SeedData.Products);
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Lets `dotnet ef` build the context without starting the app or reaching a database.</summary>
internal sealed class CatalogDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql("Host=localhost;Database=catalogdb").Options);
}

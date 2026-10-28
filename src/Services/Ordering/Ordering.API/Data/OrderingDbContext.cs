using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Ordering.API.Models;

namespace Ordering.API.Data;

public sealed class OrderingDbContext(DbContextOptions<OrderingDbContext> options) : DbContext(options)
{
    public const string ConnectionName = "orderingdb";

    internal DbSet<OrderEntity> Orders => Set<OrderEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderEntity>(order =>
        {
            order.ToTable("orders");
            order.HasKey(o => o.Id);
            order.Property(o => o.UserName).HasMaxLength(64).IsRequired();
            order.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            order.HasIndex(o => new { o.UserName, o.CreatedAt });

            // Value object stored as columns on the orders table (ShippingAddress_City, ...).
            order.ComplexProperty(o => o.ShippingAddress, address =>
            {
                address.Property(a => a.FullName).HasMaxLength(100);
                address.Property(a => a.Street).HasMaxLength(200);
                address.Property(a => a.City).HasMaxLength(100);
                address.Property(a => a.Country).HasMaxLength(100);
                address.Property(a => a.ZipCode).HasMaxLength(20);
            });

            order.HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Cascade);
            order.Navigation(o => o.Lines).AutoInclude();
        });

        modelBuilder.Entity<OrderLineEntity>(line =>
        {
            line.ToTable("order_lines");
            line.HasKey(l => l.Id);
            line.Property(l => l.ProductName).HasMaxLength(100).IsRequired();
            line.Property(l => l.UnitPrice).HasPrecision(18, 2);
        });
    }
}

// Persistence model, kept separate from the immutable API records so storage concerns
// (keys, foreign keys, mutability for EF) never leak into the public contract.

internal sealed class OrderEntity
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = "";
    public OrderStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public AddressData ShippingAddress { get; set; } = new();
    public List<OrderLineEntity> Lines { get; set; } = [];

    public static OrderEntity From(Order order) => new()
    {
        Id = order.Id,
        UserName = order.UserName,
        Status = order.Status,
        CreatedAt = order.CreatedAt,
        ShippingAddress = new AddressData
        {
            FullName = order.ShippingAddress.FullName,
            Street = order.ShippingAddress.Street,
            City = order.ShippingAddress.City,
            Country = order.ShippingAddress.Country,
            ZipCode = order.ShippingAddress.ZipCode,
        },
        Lines = order.Items.Select(i => new OrderLineEntity
        {
            OrderId = order.Id,
            ProductId = i.ProductId,
            ProductName = i.ProductName,
            UnitPrice = i.UnitPrice,
            Quantity = i.Quantity,
        }).ToList(),
    };

    public Order ToOrder() => new(
        Id,
        UserName,
        Lines.OrderBy(l => l.Id).Select(l => new OrderItem(l.ProductId, l.ProductName, l.UnitPrice, l.Quantity)).ToList(),
        new ShippingAddress(ShippingAddress.FullName, ShippingAddress.Street, ShippingAddress.City, ShippingAddress.Country, ShippingAddress.ZipCode),
        Status,
        CreatedAt);
}

internal sealed class OrderLineEntity
{
    public int Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

internal sealed class AddressData
{
    public string FullName { get; set; } = "";
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string Country { get; set; } = "";
    public string ZipCode { get; set; } = "";
}

/// <summary>Lets `dotnet ef` build the context without starting the app or reaching a database.</summary>
internal sealed class OrderingDbContextDesignTimeFactory : IDesignTimeDbContextFactory<OrderingDbContext>
{
    public OrderingDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<OrderingDbContext>().UseNpgsql("Host=localhost;Database=orderingdb").Options);
}

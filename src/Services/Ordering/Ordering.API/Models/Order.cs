using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Ordering.API.Models;

[JsonConverter(typeof(JsonStringEnumConverter<OrderStatus>))]
public enum OrderStatus
{
    Submitted,
    Paid,
    Shipped,
    Cancelled
}

public sealed record OrderItem(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity)
{
    public decimal LineTotal => UnitPrice * Quantity;
}

public sealed record ShippingAddress(
    [property: Required] string FullName,
    [property: Required] string Street,
    [property: Required] string City,
    [property: Required] string Country,
    [property: Required] string ZipCode);

public sealed record Order(
    Guid Id,
    string UserName,
    IReadOnlyList<OrderItem> Items,
    ShippingAddress ShippingAddress,
    OrderStatus Status,
    DateTimeOffset CreatedAt)
{
    public decimal Total => Items.Sum(i => i.LineTotal);
}

/// <summary>The order is created for the authenticated user.</summary>
public sealed record CreateOrderRequest(
    [property: Required, MinLength(1)] IReadOnlyList<OrderItem> Items,
    [property: Required] ShippingAddress ShippingAddress);

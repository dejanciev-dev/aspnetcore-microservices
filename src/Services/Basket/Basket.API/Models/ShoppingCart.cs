using System.ComponentModel.DataAnnotations;

namespace Basket.API.Models;

public sealed record CartItem(Guid ProductId, string ProductName, string ImageUrl, decimal UnitPrice, int Quantity)
{
    public decimal LineTotal => UnitPrice * Quantity;
}

public sealed record ShoppingCart(string UserName, IReadOnlyList<CartItem> Items)
{
    public decimal Total => Items.Sum(i => i.LineTotal);

    public static ShoppingCart Empty(string userName) => new(userName, []);
}

public sealed record AddItemRequest(Guid ProductId, [property: Range(1, 99)] int Quantity = 1);

public sealed record CheckoutRequest([property: Required] ShippingAddress ShippingAddress);

public sealed record ShippingAddress(
    [property: Required] string FullName,
    [property: Required] string Street,
    [property: Required] string City,
    [property: Required] string Country,
    [property: Required] string ZipCode);

public sealed record CheckoutResult(Guid OrderId, decimal Total);

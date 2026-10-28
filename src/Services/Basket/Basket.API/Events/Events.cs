using BuildingBlocks.Messaging;
using Basket.API.Models;

namespace Basket.API.Events;

// Basket's own copies of the events it exchanges; only the JSON shape and names must match.

/// <summary>Consumed: Catalog changed a product's price.</summary>
[EventName("catalog.product-price-changed")]
public sealed record ProductPriceChanged(Guid ProductId, decimal NewPrice, decimal OldPrice) : IntegrationEvent;

/// <summary>Published at checkout; Ordering.API turns it into an order with <see cref="OrderId"/>.</summary>
[EventName("basket.checked-out")]
public sealed record BasketCheckedOut(
    Guid OrderId,
    string UserName,
    IReadOnlyList<CheckedOutItem> Items,
    ShippingAddress ShippingAddress) : IntegrationEvent;

public sealed record CheckedOutItem(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity);

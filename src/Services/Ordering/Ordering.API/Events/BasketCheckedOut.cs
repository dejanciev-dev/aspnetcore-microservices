using BuildingBlocks.Messaging;
using Ordering.API.Models;

namespace Ordering.API.Events;

/// <summary>
/// Published by Basket.API at checkout. <see cref="OrderId"/> is chosen by the publisher, which makes
/// order creation idempotent: a redelivered message maps to the same order.
/// </summary>
[EventName("basket.checked-out")]
public sealed record BasketCheckedOut(
    Guid OrderId,
    string UserName,
    IReadOnlyList<OrderItem> Items,
    ShippingAddress ShippingAddress) : IntegrationEvent;

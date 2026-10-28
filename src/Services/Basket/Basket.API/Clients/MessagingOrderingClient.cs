using BuildingBlocks.Messaging;
using Basket.API.Events;
using Basket.API.Models;

namespace Basket.API.Clients;

/// <summary>
/// Asynchronous checkout: instead of calling Ordering.API over HTTP, publish
/// <see cref="BasketCheckedOut"/> and return the order id straight away. Checkout keeps working
/// while Ordering is down; the order appears as soon as Ordering processes the message.
/// </summary>
public sealed class MessagingOrderingClient(IEventPublisher publisher) : IOrderingClient
{
    public async Task<CheckoutResult> CreateOrderAsync(ShoppingCart cart, ShippingAddress address, CancellationToken ct = default)
    {
        var orderId = Guid.NewGuid();
        var items = cart.Items.Select(i => new CheckedOutItem(i.ProductId, i.ProductName, i.UnitPrice, i.Quantity)).ToList();

        // Completes only after the broker confirms; if it throws, the basket is left untouched.
        await publisher.PublishAsync(new BasketCheckedOut(orderId, cart.UserName, items, address), ct);
        return new CheckoutResult(orderId, cart.Total);
    }
}

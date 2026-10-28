using BuildingBlocks.Messaging;
using Ordering.API.Data;
using Ordering.API.Models;

namespace Ordering.API.Events;

public sealed class BasketCheckedOutHandler(IOrderRepository orders, TimeProvider clock, ILogger<BasketCheckedOutHandler> logger)
    : IIntegrationEventHandler<BasketCheckedOut>
{
    public async Task HandleAsync(BasketCheckedOut @event, CancellationToken ct)
    {
        var order = new Order(@event.OrderId, @event.UserName, @event.Items, @event.ShippingAddress, OrderStatus.Submitted, clock.GetUtcNow());

        if (await orders.TryAddAsync(order, ct))
            logger.LogInformation("Order {OrderId} submitted by {UserName} for {Total:C}", order.Id, order.UserName, order.Total);
        else
            logger.LogInformation("Order {OrderId} already exists; duplicate {Event} ignored", order.Id, nameof(BasketCheckedOut));
    }
}

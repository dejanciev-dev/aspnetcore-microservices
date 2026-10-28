using BuildingBlocks.Messaging;
using Basket.API.Data;

namespace Basket.API.Events;

/// <summary>Keeps prices in open baskets in line with the catalog.</summary>
public sealed class ProductPriceChangedHandler(IBasketRepository baskets, ILogger<ProductPriceChangedHandler> logger)
    : IIntegrationEventHandler<ProductPriceChanged>
{
    public async Task HandleAsync(ProductPriceChanged @event, CancellationToken ct)
    {
        var updated = await baskets.UpdatePriceAsync(@event.ProductId, @event.NewPrice, ct);
        logger.LogInformation("Repriced product {ProductId} from {OldPrice} to {NewPrice} in {Count} basket(s)",
            @event.ProductId, @event.OldPrice, @event.NewPrice, updated);
    }
}

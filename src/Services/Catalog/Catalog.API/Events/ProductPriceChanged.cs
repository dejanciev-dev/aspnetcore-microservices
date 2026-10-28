using BuildingBlocks.Messaging;

namespace Catalog.API.Events;

/// <summary>Published when a product's price changes, so baskets holding it can reprice.</summary>
[EventName("catalog.product-price-changed")]
public sealed record ProductPriceChanged(Guid ProductId, decimal NewPrice, decimal OldPrice) : IntegrationEvent;

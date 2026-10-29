using Basket.API;
using Basket.API.Clients;
using Basket.API.Models;
using BuildingBlocks.Messaging;
using PactNet.Verifier;

namespace Services.ContractTests.Providers;

/// <summary>Basket.API publishes the <c>basket.checked-out</c> event Ordering.API expects.</summary>
[Collection(PactCollection.Name)]
public sealed class BasketProviderTests
{
    [Fact]
    public async Task Basket_events_honour_the_ordering_contract()
    {
        await using var basket = new ProviderHost<BasketApiMarker>(messages: new()
        {
            ["basket.checked-out"] = _ => CheckOutAsync(),
        });

        using var verifier = new PactVerifier("Basket.API.Events", Pacts.VerifierConfig);
        verifier.WithHttpEndpoint(basket.MessagesUri)
            .WithFileSource(Pacts.File("Ordering.API", "Basket.API.Events"))
            .Verify();
    }

    /// <summary>The event exactly as Basket's real checkout path publishes it.</summary>
    private static async Task<object> CheckOutAsync()
    {
        var publisher = new CapturingPublisher();
        var cart = new ShoppingCart("ada", [new CartItem(Guid.NewGuid(), "Mechanical keyboard", "https://example.com/keyboard.png", 89.99m, 2)]);

        await new MessagingOrderingClient(publisher).CreateOrderAsync(cart, new ShippingAddress("Ada Lovelace", "12 Analytical St", "London", "UK", "N1 9GU"));

        return publisher.Published ?? throw new InvalidOperationException("Checkout published nothing.");
    }

    private sealed class CapturingPublisher : IEventPublisher
    {
        public object? Published { get; private set; }

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IntegrationEvent
        {
            Published = @event;
            return Task.CompletedTask;
        }
    }
}

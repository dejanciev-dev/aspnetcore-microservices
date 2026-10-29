using Microsoft.Extensions.Logging.Abstractions;
using Ordering.API.Data;
using Ordering.API.Events;
using PactNet;
using PactNet.Matchers;

namespace Services.ContractTests.Consumers;

/// <summary>
/// What Ordering.API needs in the <c>basket.checked-out</c> event that Basket publishes at checkout.
/// Message contracts are consumer-driven too: Ordering writes it, Basket verifies it.
/// </summary>
[Collection(PactCollection.Name)]
public sealed class OrderingConsumerTests
{
    private const string GuidPattern = "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Checked_out_baskets_become_orders()
    {
        var pact = Pact.V3("Ordering.API", "Basket.API.Events", Pacts.Config).WithMessageInteractions();
        var orders = new InMemoryOrderRepository();
        var orderId = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");

        await pact.ExpectsToReceive("basket.checked-out")
            .WithJsonContent(new
            {
                id = Match.Regex("0d9e4c1a-8b7f-4f3e-9a51-6c2d8e7f1b20", GuidPattern),
                occurredAt = Match.Type("2026-10-29T10:15:30.1234567+00:00"),
                orderId = Match.Regex(orderId.ToString(), GuidPattern),
                userName = Match.Type("ada"),
                items = Match.MinType(new
                {
                    productId = Match.Regex("6f1c0a52-3c7e-4a8e-9d0b-2b6f6d1f4a11", GuidPattern),
                    productName = Match.Type("Mechanical keyboard"),
                    unitPrice = Match.Number(89.99m),
                    quantity = Match.Integer(2),
                }, 1),
                shippingAddress = new
                {
                    fullName = Match.Type("Ada Lovelace"),
                    street = Match.Type("12 Analytical St"),
                    city = Match.Type("London"),
                    country = Match.Type("UK"),
                    zipCode = Match.Type("N1 9GU"),
                },
            })
            .VerifyAsync<BasketCheckedOut>(async @event =>
                await new BasketCheckedOutHandler(orders, TimeProvider.System, NullLogger<BasketCheckedOutHandler>.Instance).HandleAsync(@event, Ct));

        var order = await orders.GetAsync(orderId, Ct);
        Assert.NotNull(order);
        Assert.Equal("ada", order.UserName);
        Assert.Equal(179.98m, order.Total);
    }
}

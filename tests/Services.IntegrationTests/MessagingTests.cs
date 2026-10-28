using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Basket.API;
using Basket.API.Clients;
using Basket.API.Models;
using BuildingBlocks.Messaging;
using Catalog.API;
using Catalog.API.Data;
using Catalog.API.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.API;
using RabbitMQ.Client;
using OrderingModels = Ordering.API.Models;

namespace Services.IntegrationTests;

// Queue names are fixed per service ("basket-api.…"), so app instances from parallel tests would
// compete for each other's messages. Keep all messaging tests in one serial collection.
[CollectionDefinition(nameof(MessagingCollection), DisableParallelization = true)]
public sealed class MessagingCollection;

/// <summary>End-to-end flows through a real RabbitMQ, PostgreSQL and Redis.</summary>
[Collection(nameof(MessagingCollection))]
public sealed class MessagingTests(Infrastructure infra)
{
    private static readonly Guid KeyboardId = Guid.Parse("4f1c9a0e-0001-4a6e-9b1a-000000000001");   // seeded in Catalog
    private static readonly OrderingModels.ShippingAddress OrderAddress = new("Ada Lovelace", "1 Main St", "London", "UK", "N1 9GU");
    private static readonly ShippingAddress BasketAddress = new("Ada Lovelace", "1 Main St", "London", "UK", "N1 9GU");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_price_change_in_catalog_reprices_open_baskets()
    {
        await using var catalog = CatalogApp();
        await using var basket = BasketApp();
        var catalogClient = catalog.CreateClient();
        var basketClient = basket.CreateClient();
        await WaitForQueueAsync("basket-api.catalog.product-price-changed");

        var user = $"user-{Guid.NewGuid():N}";
        await basketClient.PostAsJsonAsync($"/api/basket/{user}/items", new AddItemRequest(KeyboardId, 2), Ct);

        // Catalog writes the new price and a ProductPriceChanged outbox row in one transaction;
        // the relay publishes it and Basket's consumer updates the cart in Redis.
        var update = new UpsertProductRequest("Mechanical Keyboard", "Hot-swappable 75% keyboard.", "Peripherals", 99.00m, null);
        Assert.Equal(HttpStatusCode.NoContent, (await catalogClient.PutAsJsonAsync($"/api/products/{KeyboardId}", update, Ct)).StatusCode);

        var cart = await EventuallyAsync(
            () => basketClient.GetFromJsonAsync<ShoppingCart>($"/api/basket/{user}", Ct),
            c => c?.Items.Single().UnitPrice == 99.00m);

        Assert.Equal(198.00m, cart!.Total);
    }

    [Fact]
    public async Task Saving_a_product_without_a_price_change_stages_no_event()
    {
        await using var catalog = CatalogApp();
        var update = new UpsertProductRequest("Renamed Keyboard", "Same price.", "Peripherals", 129.99m, null);

        await catalog.CreateClient().PutAsJsonAsync($"/api/products/{KeyboardId}", update, Ct);

        await using var scope = catalog.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.Equal(0, await db.Set<OutboxMessage>().CountAsync(Ct));
    }

    [Fact]
    public async Task Checkout_flows_through_rabbitmq_into_ordering()
    {
        await using var ordering = OrderingApp();
        await using var basket = BasketApp();
        var orderingClient = ordering.CreateClient();
        var basketClient = basket.CreateClient();
        await WaitForQueueAsync("ordering-api.basket.checked-out");

        var user = $"user-{Guid.NewGuid():N}";
        await basketClient.PostAsJsonAsync($"/api/basket/{user}/items", new AddItemRequest(KeyboardId, 3), Ct);
        var checkout = await basketClient.PostAsJsonAsync($"/api/basket/{user}/checkout", new CheckoutRequest(BasketAddress), Ct);
        var result = await checkout.Content.ReadFromJsonAsync<CheckoutResult>(Ct);

        // Basket answers immediately with the order id; Ordering creates the order from the message.
        Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);
        var order = await EventuallyAsync(
            async () => (await orderingClient.GetAsync($"/api/orders/{result!.OrderId}", Ct)) is { IsSuccessStatusCode: true } r
                ? await r.Content.ReadFromJsonAsync<OrderingModels.Order>(Ct)
                : null,
            o => o is not null);

        Assert.Equal(user, order!.UserName);
        Assert.Equal(3 * 129.99m, order.Total);
        Assert.Equal(OrderAddress, order.ShippingAddress);
        Assert.Empty((await basketClient.GetFromJsonAsync<ShoppingCart>($"/api/basket/{user}", Ct))!.Items);
    }

    [Fact]
    public async Task A_redelivered_checkout_message_creates_exactly_one_order()
    {
        await using var ordering = OrderingApp();
        var client = ordering.CreateClient();
        await WaitForQueueAsync("ordering-api.basket.checked-out");

        var orderId = Guid.NewGuid();
        var user = $"user-{Guid.NewGuid():N}";
        var message = new
        {
            id = Guid.NewGuid(),
            occurredAt = DateTimeOffset.UtcNow,
            orderId,
            userName = user,
            items = new[] { new { productId = KeyboardId, productName = "Keyboard", unitPrice = 10m, quantity = 1 } },
            shippingAddress = OrderAddress,
        };

        // At-least-once delivery: the same event can arrive twice.
        await PublishAsync("basket.checked-out", message);
        await PublishAsync("basket.checked-out", message);

        await EventuallyAsync(() => client.GetFromJsonAsync<List<OrderingModels.Order>>($"/api/orders?userName={user}", Ct), o => o?.Count > 0);
        await Task.Delay(TimeSpan.FromSeconds(2), Ct);   // give the duplicate time to be processed

        var orders = await client.GetFromJsonAsync<List<OrderingModels.Order>>($"/api/orders?userName={user}", Ct);
        Assert.Equal(orderId, Assert.Single(orders!).Id);
    }

    // ---------- apps ----------

    private WebApplicationFactory<CatalogApiMarker> CatalogApp() =>
        new WebApplicationFactory<CatalogApiMarker>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:catalogdb", infra.PostgresConnectionString($"catalog_{Guid.NewGuid():N}"))
            .UseSetting("ConnectionStrings:rabbitmq", infra.RabbitMqConnectionString));

    private WebApplicationFactory<OrderingApiMarker> OrderingApp() =>
        new WebApplicationFactory<OrderingApiMarker>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:orderingdb", infra.PostgresConnectionString($"ordering_{Guid.NewGuid():N}"))
            .UseSetting("ConnectionStrings:rabbitmq", infra.RabbitMqConnectionString));

    // Real Redis and RabbitMQ; only the Catalog HTTP lookup is faked (it returns the seeded keyboard).
    private WebApplicationFactory<BasketApiMarker> BasketApp() =>
        new WebApplicationFactory<BasketApiMarker>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:redis", infra.RedisConnectionString)
            .UseSetting("ConnectionStrings:rabbitmq", infra.RabbitMqConnectionString)
            .ConfigureTestServices(s => s.AddSingleton<ICatalogClient>(new SeededCatalog())));

    private sealed class SeededCatalog : ICatalogClient
    {
        public Task<CatalogProduct?> GetProductAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(id == KeyboardId ? new CatalogProduct(KeyboardId, "Mechanical Keyboard", "", 129.99m) : null);
    }

    // ---------- broker helpers ----------

    private async Task<IConnection> ConnectAsync() =>
        await new ConnectionFactory { Uri = new Uri(infra.RabbitMqConnectionString) }.CreateConnectionAsync(Ct);

    /// <summary>Consumers declare their queues in the background at startup; wait until it exists.</summary>
    private async Task WaitForQueueAsync(string queue)
    {
        await using var connection = await ConnectAsync();
        await EventuallyAsync(async () =>
        {
            try
            {
                await using var channel = await connection.CreateChannelAsync(cancellationToken: Ct);
                var ok = await channel.QueueDeclarePassiveAsync(queue, Ct);
                return ok.ConsumerCount > 0;
            }
            catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
            {
                return false;   // passive declare of a missing queue closes the channel
            }
        }, ready => ready);
    }

    private async Task PublishAsync(string routingKey, object message)
    {
        await using var connection = await ConnectAsync();
        await using var channel = await connection.CreateChannelAsync(cancellationToken: Ct);
        var body = JsonSerializer.SerializeToUtf8Bytes(message, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await channel.BasicPublishAsync(RabbitMqTopology.Exchange, routingKey, body, Ct);
    }

    private static async Task<T> EventuallyAsync<T>(Func<Task<T>> probe, Func<T, bool> done, int timeoutSeconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (true)
        {
            var value = await probe();
            if (done(value)) return value;
            if (DateTime.UtcNow > deadline) Assert.Fail($"Condition not met within {timeoutSeconds}s (last value: {JsonSerializer.Serialize(value)}).");
            await Task.Delay(250, Ct);
        }
    }
}

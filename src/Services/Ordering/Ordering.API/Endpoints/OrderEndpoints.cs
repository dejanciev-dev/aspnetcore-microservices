using Microsoft.AspNetCore.Http.HttpResults;
using Ordering.API.Data;
using Ordering.API.Models;

namespace Ordering.API.Endpoints;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders").WithTags("Orders");

        group.MapGet("/", async (string userName, IOrderRepository repo, CancellationToken ct) =>
                TypedResults.Ok(await repo.ListByUserAsync(userName, ct)))
            .WithName("ListOrders")
            .WithSummary("List a user's orders, newest first");

        group.MapGet("/{id:guid}", async Task<Results<Ok<Order>, NotFound>> (Guid id, IOrderRepository repo, CancellationToken ct) =>
            await repo.GetAsync(id, ct) is { } order ? TypedResults.Ok(order) : TypedResults.NotFound())
            .WithName("GetOrder");

        group.MapPost("/", async (CreateOrderRequest request, IOrderRepository repo, TimeProvider clock, ILogger<Order> logger, CancellationToken ct) =>
        {
            var order = await repo.AddAsync(new Order(
                Guid.NewGuid(),
                request.UserName,
                request.Items,
                request.ShippingAddress,
                OrderStatus.Submitted,
                clock.GetUtcNow()), ct);

            logger.LogInformation("Order {OrderId} submitted by {UserName} for {Total:C}", order.Id, order.UserName, order.Total);
            return TypedResults.CreatedAtRoute(order, "GetOrder", new { id = order.Id });
        })
        .WithName("CreateOrder");

        return app;
    }
}

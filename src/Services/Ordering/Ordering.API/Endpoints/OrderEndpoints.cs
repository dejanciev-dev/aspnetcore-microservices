using Microsoft.AspNetCore.Http.HttpResults;
using Ordering.API.Data;
using Ordering.API.Models;

namespace Ordering.API.Endpoints;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders").WithTags("Orders");

        group.MapGet("/", (string userName, IOrderRepository repo) => TypedResults.Ok(repo.ListByUser(userName)))
            .WithName("ListOrders")
            .WithSummary("List a user's orders, newest first");

        group.MapGet("/{id:guid}", Results<Ok<Order>, NotFound> (Guid id, IOrderRepository repo) =>
            repo.Get(id) is { } order ? TypedResults.Ok(order) : TypedResults.NotFound())
            .WithName("GetOrder");

        group.MapPost("/", (CreateOrderRequest request, IOrderRepository repo, TimeProvider clock, ILogger<Order> logger) =>
        {
            var order = repo.Add(new Order(
                Guid.NewGuid(),
                request.UserName,
                request.Items,
                request.ShippingAddress,
                OrderStatus.Submitted,
                clock.GetUtcNow()));

            logger.LogInformation("Order {OrderId} submitted by {UserName} for {Total:C}", order.Id, order.UserName, order.Total);
            return TypedResults.CreatedAtRoute(order, "GetOrder", new { id = order.Id });
        })
        .WithName("CreateOrder");

        return app;
    }
}

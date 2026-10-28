using System.Security.Claims;
using BuildingBlocks.Common;
using Microsoft.AspNetCore.Http.HttpResults;
using Ordering.API.Data;
using Ordering.API.Models;

namespace Ordering.API.Endpoints;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders").WithTags("Orders").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, IOrderRepository repo, CancellationToken ct) =>
                TypedResults.Ok(await repo.ListByUserAsync(user.UserName(), ct)))
            .WithName("ListOrders")
            .WithSummary("List the signed-in user's orders, newest first");

        // Someone else's order is reported as 404, not 403, so order ids can't be probed. Admins see all.
        group.MapGet("/{id:guid}", async Task<Results<Ok<Order>, NotFound>> (Guid id, ClaimsPrincipal user, IOrderRepository repo, CancellationToken ct) =>
                await repo.GetAsync(id, ct) is { } order && (IsOwner(order, user) || user.IsInRole("admin"))
                    ? TypedResults.Ok(order)
                    : TypedResults.NotFound())
            .WithName("GetOrder");

        group.MapPost("/", async (CreateOrderRequest request, ClaimsPrincipal user, IOrderRepository repo, TimeProvider clock, ILogger<Order> logger, CancellationToken ct) =>
        {
            var order = new Order(
                Guid.NewGuid(),
                user.UserName(),
                request.Items,
                request.ShippingAddress,
                OrderStatus.Submitted,
                clock.GetUtcNow());
            await repo.TryAddAsync(order, ct);

            logger.LogInformation("Order {OrderId} submitted by {UserName} for {Total:C}", order.Id, order.UserName, order.Total);
            return TypedResults.CreatedAtRoute(order, "GetOrder", new { id = order.Id });
        })
        .WithName("CreateOrder");

        return app;
    }

    private static bool IsOwner(Order order, ClaimsPrincipal user) =>
        string.Equals(order.UserName, user.Identity?.Name, StringComparison.OrdinalIgnoreCase);
}

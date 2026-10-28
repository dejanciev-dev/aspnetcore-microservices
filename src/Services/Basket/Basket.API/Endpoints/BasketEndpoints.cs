using System.Security.Claims;
using Basket.API.Clients;
using Basket.API.Data;
using Basket.API.Models;
using BuildingBlocks.Common;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Basket.API.Endpoints;

public static class BasketEndpoints
{
    public static IEndpointRouteBuilder MapBasketEndpoints(this IEndpointRouteBuilder app)
    {
        // The basket always belongs to the signed-in user; the name comes from the validated token.
        var group = app.MapGroup("/api/basket").WithTags("Basket").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, IBasketRepository repo, CancellationToken ct) =>
                TypedResults.Ok(await repo.GetAsync(user.UserName(), ct)))
            .WithName("GetBasket");

        group.MapPost("/items", async Task<Results<Ok<ShoppingCart>, ValidationProblem>> (
            ClaimsPrincipal user, AddItemRequest request, IBasketRepository repo, ICatalogClient catalog, CancellationToken ct) =>
        {
            // Never trust client-side prices: resolve the product from Catalog.API.
            var product = await catalog.GetProductAsync(request.ProductId, ct);
            if (product is null)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.ProductId)] = [$"Product '{request.ProductId}' does not exist."]
                });

            var cart = await repo.GetAsync(user.UserName(), ct);
            var existing = cart.Items.FirstOrDefault(i => i.ProductId == product.Id);
            var quantity = (existing?.Quantity ?? 0) + request.Quantity;

            var items = cart.Items.Where(i => i.ProductId != product.Id)
                .Append(new CartItem(product.Id, product.Name, product.ImageUrl, product.Price, quantity))
                .ToList();

            return TypedResults.Ok(await repo.SaveAsync(cart with { Items = items }, ct));
        })
        .WithName("AddBasketItem");

        group.MapDelete("/items/{productId:guid}", async (ClaimsPrincipal user, Guid productId, IBasketRepository repo, CancellationToken ct) =>
        {
            var cart = await repo.GetAsync(user.UserName(), ct);
            return TypedResults.Ok(await repo.SaveAsync(cart with { Items = cart.Items.Where(i => i.ProductId != productId).ToList() }, ct));
        })
        .WithName("RemoveBasketItem");

        group.MapDelete("/", async (ClaimsPrincipal user, IBasketRepository repo, CancellationToken ct) =>
        {
            await repo.DeleteAsync(user.UserName(), ct);
            return TypedResults.NoContent();
        })
        .WithName("ClearBasket");

        group.MapPost("/checkout", async Task<Results<Ok<CheckoutResult>, ValidationProblem>> (
            ClaimsPrincipal user, CheckoutRequest request, IBasketRepository repo, IOrderingClient ordering, CancellationToken ct) =>
        {
            var cart = await repo.GetAsync(user.UserName(), ct);
            if (cart.Items.Count == 0)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Basket"] = ["Cannot check out an empty basket."]
                });

            var result = await ordering.CreateOrderAsync(cart, request.ShippingAddress, ct);
            await repo.DeleteAsync(user.UserName(), ct);
            return TypedResults.Ok(result);
        })
        .WithName("Checkout");

        return app;
    }
}

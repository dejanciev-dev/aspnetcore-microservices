using Basket.API.Clients;
using Basket.API.Data;
using Basket.API.Models;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Basket.API.Endpoints;

public static class BasketEndpoints
{
    public static IEndpointRouteBuilder MapBasketEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/basket/{userName}").WithTags("Basket");

        group.MapGet("/", (string userName, IBasketRepository repo) => TypedResults.Ok(repo.Get(userName)))
            .WithName("GetBasket");

        group.MapPost("/items", async Task<Results<Ok<ShoppingCart>, ValidationProblem>> (
            string userName, AddItemRequest request, IBasketRepository repo, ICatalogClient catalog, CancellationToken ct) =>
        {
            // Never trust client-side prices: resolve the product from Catalog.API.
            var product = await catalog.GetProductAsync(request.ProductId, ct);
            if (product is null)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.ProductId)] = [$"Product '{request.ProductId}' does not exist."]
                });

            var cart = repo.Get(userName);
            var existing = cart.Items.FirstOrDefault(i => i.ProductId == product.Id);
            var quantity = (existing?.Quantity ?? 0) + request.Quantity;

            var items = cart.Items.Where(i => i.ProductId != product.Id)
                .Append(new CartItem(product.Id, product.Name, product.ImageUrl, product.Price, quantity))
                .ToList();

            return TypedResults.Ok(repo.Save(cart with { Items = items }));
        })
        .WithName("AddBasketItem");

        group.MapDelete("/items/{productId:guid}", (string userName, Guid productId, IBasketRepository repo) =>
        {
            var cart = repo.Get(userName);
            return TypedResults.Ok(repo.Save(cart with { Items = cart.Items.Where(i => i.ProductId != productId).ToList() }));
        })
        .WithName("RemoveBasketItem");

        group.MapDelete("/", (string userName, IBasketRepository repo) =>
        {
            repo.Delete(userName);
            return TypedResults.NoContent();
        })
        .WithName("ClearBasket");

        group.MapPost("/checkout", async Task<Results<Ok<CheckoutResult>, ValidationProblem>> (
            string userName, CheckoutRequest request, IBasketRepository repo, IOrderingClient ordering, CancellationToken ct) =>
        {
            var cart = repo.Get(userName);
            if (cart.Items.Count == 0)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Basket"] = ["Cannot check out an empty basket."]
                });

            var result = await ordering.CreateOrderAsync(cart, request.ShippingAddress, ct);
            repo.Delete(userName);
            return TypedResults.Ok(result);
        })
        .WithName("Checkout");

        return app;
    }
}

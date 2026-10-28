using BuildingBlocks.Common;
using Catalog.API.Data;
using Catalog.API.Models;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Catalog.API.Endpoints;

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products").WithTags("Products");

        group.MapGet("/", async (IProductRepository repo, string? category, string? search, CancellationToken ct, int page = 1, int pageSize = 12) =>
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 100);
            return TypedResults.Ok(await repo.ListAsync(category, search, page, pageSize, ct));
        })
        .WithName("ListProducts")
        .WithSummary("List products with optional filtering and paging");

        group.MapGet("/categories", async (IProductRepository repo, CancellationToken ct) => TypedResults.Ok(await repo.CategoriesAsync(ct)))
            .WithName("ListCategories");

        group.MapGet("/{id:guid}", async Task<Results<Ok<Product>, NotFound>> (Guid id, IProductRepository repo, CancellationToken ct) =>
            await repo.GetAsync(id, ct) is { } product ? TypedResults.Ok(product) : TypedResults.NotFound())
            .WithName("GetProduct");

        group.MapPost("/", async (UpsertProductRequest request, IProductRepository repo, CancellationToken ct) =>
        {
            var product = await repo.AddAsync(request.ToProduct(Guid.NewGuid()), ct);
            return TypedResults.CreatedAtRoute(product, "GetProduct", new { id = product.Id });
        })
        .WithName("CreateProduct");

        group.MapPut("/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, UpsertProductRequest request, IProductRepository repo, CancellationToken ct) =>
            await repo.UpdateAsync(request.ToProduct(id), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .WithName("UpdateProduct");

        group.MapDelete("/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, IProductRepository repo, CancellationToken ct) =>
            await repo.DeleteAsync(id, ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .WithName("DeleteProduct");

        return app;
    }

    private static Product ToProduct(this UpsertProductRequest r, Guid id) =>
        new(id, r.Name, r.Description, r.Category, r.Price, r.ImageUrl ?? string.Empty);
}

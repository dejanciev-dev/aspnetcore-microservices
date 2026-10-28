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

        group.MapGet("/", (IProductRepository repo, string? category, string? search, int page = 1, int pageSize = 12) =>
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 100);
            return TypedResults.Ok(repo.List(category, search, page, pageSize));
        })
        .WithName("ListProducts")
        .WithSummary("List products with optional filtering and paging");

        group.MapGet("/categories", (IProductRepository repo) => TypedResults.Ok(repo.Categories()))
            .WithName("ListCategories");

        group.MapGet("/{id:guid}", Results<Ok<Product>, NotFound> (Guid id, IProductRepository repo) =>
            repo.Get(id) is { } product ? TypedResults.Ok(product) : TypedResults.NotFound())
            .WithName("GetProduct");

        group.MapPost("/", (UpsertProductRequest request, IProductRepository repo) =>
        {
            var product = repo.Add(request.ToProduct(Guid.NewGuid()));
            return TypedResults.CreatedAtRoute(product, "GetProduct", new { id = product.Id });
        })
        .WithName("CreateProduct");

        group.MapPut("/{id:guid}", Results<NoContent, NotFound> (Guid id, UpsertProductRequest request, IProductRepository repo) =>
            repo.Update(request.ToProduct(id)) ? TypedResults.NoContent() : TypedResults.NotFound())
            .WithName("UpdateProduct");

        group.MapDelete("/{id:guid}", Results<NoContent, NotFound> (Guid id, IProductRepository repo) =>
            repo.Delete(id) ? TypedResults.NoContent() : TypedResults.NotFound())
            .WithName("DeleteProduct");

        return app;
    }

    private static Product ToProduct(this UpsertProductRequest r, Guid id) =>
        new(id, r.Name, r.Description, r.Category, r.Price, r.ImageUrl ?? string.Empty);
}

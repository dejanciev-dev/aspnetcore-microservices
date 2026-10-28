using System.ComponentModel.DataAnnotations;

namespace Catalog.API.Models;

public sealed record Product(
    Guid Id,
    string Name,
    string Description,
    string Category,
    decimal Price,
    string ImageUrl);

public sealed record UpsertProductRequest(
    [property: Required, StringLength(100, MinimumLength = 2)] string Name,
    [property: StringLength(1000)] string Description,
    [property: Required, StringLength(50)] string Category,
    [property: Range(0.01, 1_000_000)] decimal Price,
    [property: Url] string? ImageUrl);

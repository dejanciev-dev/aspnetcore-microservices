using Catalog.API;
using Catalog.API.Data;
using Catalog.API.Events;
using Catalog.API.Models;
using Microsoft.Extensions.DependencyInjection;
using PactNet.Verifier;

namespace Services.ContractTests.Providers;

/// <summary>Catalog.API honours what Basket.API expects from its HTTP API and its events.</summary>
[Collection(PactCollection.Name)]
public sealed class CatalogProviderTests
{
    [Fact]
    public async Task Catalog_api_honours_the_basket_contract()
    {
        await using var catalog = new ProviderHost<CatalogApiMarker>(states: new()
        {
            ["a product exists"] = (services, p) => services.GetRequiredService<IProductRepository>().AddAsync(
                new Product(Guid.Parse(p["id"]), "Contract keyboard", "Seeded for the pact", "Accessories", 49.50m, "https://example.com/k.png")),
            ["no product exists"] = (services, p) => services.GetRequiredService<IProductRepository>().DeleteAsync(Guid.Parse(p["id"])),
        });

        using var verifier = new PactVerifier("Catalog.API", Pacts.VerifierConfig);
        verifier.WithHttpEndpoint(catalog.BaseUri)
            .WithFileSource(Pacts.File("Basket.API", "Catalog.API"))
            .WithProviderStateUrl(catalog.StateUri)
            .Verify();
    }

    [Fact]
    public async Task Catalog_events_honour_the_basket_contract()
    {
        await using var catalog = new ProviderHost<CatalogApiMarker>(messages: new()
        {
            ["catalog.product-price-changed"] = _ => Task.FromResult<object>(new ProductPriceChanged(Guid.NewGuid(), 79.99m, 89.99m)),
        });

        using var verifier = new PactVerifier("Catalog.API.Events", Pacts.VerifierConfig);
        verifier.WithHttpEndpoint(catalog.MessagesUri)
            .WithFileSource(Pacts.File("Basket.API", "Catalog.API.Events"))
            .Verify();
    }
}

using Ordering.API;
using PactNet.Verifier;

namespace Services.ContractTests.Providers;

/// <summary>Ordering.API honours what Basket.API expects when it creates orders over HTTP.</summary>
[Collection(PactCollection.Name)]
public sealed class OrderingProviderTests
{
    [Fact]
    public async Task Ordering_api_honours_the_basket_contract()
    {
        await using var ordering = new ProviderHost<OrderingApiMarker>();

        using var verifier = new PactVerifier("Ordering.API", Pacts.VerifierConfig);
        verifier.WithHttpEndpoint(ordering.BaseUri)
            .WithFileSource(Pacts.File("Basket.API", "Ordering.API"))
            .WithProviderStateUrl(ordering.StateUri)
            .Verify();
    }
}

using System.Net;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace CrossService.IntegrationTests;

/// <summary>Each service must map only its own FastEndpoints routes when multiple hosts share a process.</summary>
[Collection("SharedTestcontainers")]
public sealed class PurchaseEndpointAssemblyIsolationTests(SharedTestcontainersFixture fixture)
{
    [Fact]
    public async Task CatalogHost_DoesNotRegisterBillingPaymentIntentRoute()
    {
        using var harness = new ProductionLifecycleHarness(fixture);
        using var response = await harness.Catalog.PostAsync($"/purchases/{Guid.NewGuid():D}/payment-intent", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

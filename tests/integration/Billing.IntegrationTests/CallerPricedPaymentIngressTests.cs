using System.Net;
using System.Net.Http.Json;
using Billings.Application.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Billing.IntegrationTests;

/// <summary>Guards against restoring a public caller-priced payment ingress.</summary>
[Collection("SharedTestcontainers")]
public sealed class CallerPricedPaymentIngressTests : BillingIntegrationTestBase
{
    /// <summary>Initializes the route regression test.</summary>
    /// <param name="fixture">The shared PostgreSQL fixture.</param>
    public CallerPricedPaymentIngressTests(SharedTestcontainersFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task PostPayments_WithCallerAmountAndCurrency_ReturnsRouteAbsent()
    {
        var response = await Client.PostAsJsonAsync("/payments", new { OrderId = Guid.NewGuid(), Amount = 999m, Currency = "USD" });

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
        Assert.Equal(0, Provider.AttemptCalls);
        Assert.Equal(0, Provider.CaptureCalls);

        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        Assert.Empty(await context.Payments.IgnoreQueryFilters().ToListAsync());
    }
}

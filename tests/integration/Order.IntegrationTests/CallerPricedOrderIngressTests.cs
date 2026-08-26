using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Orders.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class CallerPricedOrderIngressTests(SharedTestcontainersFixture fixture) : OrderIntegrationTestBase(fixture)
{
    [Fact]
    public async Task PostOrders_WithCallerUnitPrice_IsAbsentAndCreatesNoOrder()
    {
        using var scope = CreateTenantScope();
        var context = scope.ServiceProvider.GetRequiredService<Orders.Application.Database.OrderDbContext>();
        var orderCountBefore = await context.Orders.CountAsync();
        var response = await Client.PostAsJsonAsync("/orders", new
        {
            customerId = Guid.NewGuid(),
            lines = new[] { new { productId = Guid.NewGuid(), productName = "Caller Price", quantity = 1, unitPrice = 999m } },
        });

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
        Assert.Equal(orderCountBefore, await context.Orders.CountAsync());
    }
}

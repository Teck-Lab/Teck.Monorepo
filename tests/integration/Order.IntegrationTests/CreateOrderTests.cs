using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Orders.Application.Orders.Responses;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Orders.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class CreateOrderTests : OrderIntegrationTestBase
{
    public CreateOrderTests(SharedTestcontainersFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task PostOrders_WithValidBody_ReturnsCreatedOrder()
    {
        var response = await Client.PostAsJsonAsync(
            "/orders",
            new
            {
                customerId = Guid.NewGuid(),
                lines = new[]
                {
                    new
                    {
                        productId = Guid.NewGuid(),
                        productName = "Test Product",
                        quantity = 2,
                        unitPrice = 19.95m,
                    },
                },
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var order = await response.Content.ReadFromJsonAsync<OrderDto>();

        Assert.NotNull(order);
        Assert.NotEqual(Guid.Empty, order!.Id);
        Assert.Equal(1, order.Lines.Count);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal($"/orders/{order.Id}", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task PostOrders_WithEmptyLines_ReturnsBadRequest()
    {
        var response = await Client.PostAsJsonAsync(
            "/orders",
            new
            {
                customerId = Guid.NewGuid(),
                lines = Array.Empty<object>(),
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetOrders_AfterCreation_ReturnsCreatedOrder()
    {
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var createResponse = await Client.PostAsJsonAsync(
            "/orders",
            new
            {
                customerId,
                lines = new[]
                {
                    new
                    {
                        productId,
                        productName = "Test Product",
                        quantity = 3,
                        unitPrice = 12.5m,
                    },
                },
            });

        createResponse.EnsureSuccessStatusCode();

        var createdOrder = await createResponse.Content.ReadFromJsonAsync<OrderDto>();

        Assert.NotNull(createdOrder);

        var getResponse = await Client.GetAsync($"/orders/{createdOrder!.Id}");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var order = await getResponse.Content.ReadFromJsonAsync<OrderDto>();

        Assert.NotNull(order);
        Assert.Equal(createdOrder.Id, order!.Id);
        Assert.Equal(createdOrder.CustomerId, order.CustomerId);
        Assert.Equal(createdOrder.Total, order.Total);
        Assert.Single(order.Lines);
        Assert.Equal(productId, order.Lines[0].ProductId);
    }
}

public abstract class OrderIntegrationTestBase : IDisposable
{
    private readonly SharedTestcontainersFixture fixture;
    private readonly string databaseConnectionString;
    private readonly WebApplicationFactory<Program> factory;

    protected OrderIntegrationTestBase(SharedTestcontainersFixture fixture)
    {
        this.fixture = fixture;

        Type dbContextType = Type.GetType(
            "Orders.Application.Database.OrderDbContext, Order.Application",
            throwOnError: true)!;

        databaseConnectionString = fixture
            .CreateSharedTestDatabaseAsync(dbContextType, dbContextType.Assembly.GetName().Name!)
            .GetAwaiter()
            .GetResult();

        factory = new OrderWebApplicationFactory(fixture, databaseConnectionString);
        Client = factory.CreateClient();
    }

    protected HttpClient Client { get; }

    public void Dispose()
    {
        Client.Dispose();
        factory.Dispose();
        fixture.TruncateAllTablesAsync(databaseConnectionString).GetAwaiter().GetResult();
    }

    private sealed class OrderWebApplicationFactory(
        SharedTestcontainersFixture fixture,
        string databaseConnectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:PostgreSql"] = databaseConnectionString,
                        ["ConnectionStrings:RabbitMq"] = fixture.RabbitMqConnectionString,
                    });
            });
        }
    }
}

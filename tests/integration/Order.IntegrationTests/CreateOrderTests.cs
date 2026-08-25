using System.Net;
using System.Net.Http.Json;
using ErrorOr;
using Finbuckle.MultiTenant.Abstractions;
using Finbuckle.MultiTenant.Extensions;
using JasperFx.CommandLine;
using Keycloak.AuthServices.Authorization.Requirements;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orders.Application.Orders.EventHandlers.IntegrationEvents;
using Orders.Application.Orders.Features.RetryPayment.V1;
using Orders.Application.Orders.Responses;
using Orders.Domain.Entities;
using Orders.Domain.ValueObjects;
using Orders.Host.Infrastructure;
using SharedKernel.Events;
using SharedKernel.Infrastructure.MultiTenant;
using Teck.Platform.IntegrationTests.Shared;
using Wolverine;
using Wolverine.Tracking;
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
    public async Task GetOrders_AfterV2Checkout_ReturnsPlatformPricedOrder()
    {
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var orderId = await DeliverV2CheckoutAsync(customerId, productId);

        var getResponse = await Client.GetAsync($"/orders/{orderId}");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var order = await getResponse.Content.ReadFromJsonAsync<OrderDto>();
        Assert.NotNull(order);
        Assert.Equal(orderId, order!.Id);
        Assert.Equal(customerId, order.CustomerId);
        Assert.Equal(37.5m, order.Total);
        Assert.Equal(40m, order.AuthorizedAmount);
        Assert.Single(order.Lines);
        Assert.Equal(productId, order.Lines[0].ProductId);
        Assert.Equal("Test Product", order.Lines[0].ProductName);
        Assert.Equal(3, order.Lines[0].Quantity);
        Assert.Equal(12.5m, order.Lines[0].UnitPrice);
    }

    [Fact]
    public async Task GetOrder_ForeignTenantClaimAndHeader_IsNotFound()
    {
        const string otherTenantId = "00000000-0000-0000-0000-000000000002";
        var orderId = await DeliverV2CheckoutAsync(Guid.NewGuid(), Guid.NewGuid(), otherTenantId);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/orders/{orderId}");
        request.Headers.Add("X-TenantId", otherTenantId);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RetryPaymentEndpoint_SameSubjectPublishesOnceAndPreservesCeiling()
    {
        var orderId = await DeliverV2CheckoutAsync(Guid.NewGuid(), Guid.NewGuid());
        await MarkPaymentActionRequiredAsync(orderId);

        var first = await SendRetryAsync(orderId, "retry-owner");
        var duplicate = await SendRetryAsync(orderId, "retry-owner");
        var order = await ReadOrderAsync(orderId);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, duplicate.StatusCode);
        Assert.Equal(PaymentState.Pending, order.PaymentState);
        Assert.Equal("retry-owner", order.RetryRequestId);
        Assert.Equal(40m, order.AuthorizedAmount);
        Assert.True(order.HasRecordedRetryRequest("retry-owner"));
    }

    [Fact]
    public async Task RetryPaymentEndpoint_MissingOrCrossSubject_IsForbiddenWithoutRetry()
    {
        var missingSubjectOrderId = await DeliverV2CheckoutAsync(Guid.NewGuid(), Guid.NewGuid());
        var crossSubjectOrderId = await DeliverV2CheckoutAsync(Guid.NewGuid(), Guid.NewGuid());
        await MarkPaymentActionRequiredAsync(missingSubjectOrderId);
        await MarkPaymentActionRequiredAsync(crossSubjectOrderId);

        var missing = await SendRetryAsync(missingSubjectOrderId, "retry-missing", omitSubject: true);
        var cross = await SendRetryAsync(crossSubjectOrderId, "retry-cross", subject: "subject-other");

        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, cross.StatusCode);
        Assert.Equal(PaymentState.ActionRequired, (await ReadOrderAsync(missingSubjectOrderId)).PaymentState);
        Assert.Equal(PaymentState.ActionRequired, (await ReadOrderAsync(crossSubjectOrderId)).PaymentState);
        Assert.False((await ReadOrderAsync(missingSubjectOrderId)).HasRecordedRetryRequest("retry-missing"));
        Assert.False((await ReadOrderAsync(crossSubjectOrderId)).HasRecordedRetryRequest("retry-cross"));
    }

    [Fact]
    public async Task RetryPaymentEndpoint_TenantFilteredOrder_IsNotFound()
    {
        const string otherTenantId = "00000000-0000-0000-0000-000000000002";
        var orderId = await DeliverV2CheckoutAsync(Guid.NewGuid(), Guid.NewGuid(), otherTenantId);
        await MarkPaymentActionRequiredAsync(orderId);

        var (response, tracking) = await SendRetryWithTrackingAsync(orderId, "retry-cross-tenant");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(PaymentState.ActionRequired, (await ReadOrderAsync(orderId)).PaymentState);
        Assert.False((await ReadOrderAsync(orderId)).HasRecordedRetryRequest("retry-cross-tenant"));
        Assert.Empty(tracking.Sent.MessagesOf<PaymentRetryRequestedIntegrationEvent>());
    }

    [Fact]
    public async Task RetryPaymentEndpoint_InvalidOrIneligibleRequest_IsBadRequestWithoutRetry()
    {
        var invalidOrderId = await DeliverV2CheckoutAsync(Guid.NewGuid(), Guid.NewGuid());
        var ineligibleOrderId = await DeliverV2CheckoutAsync(Guid.NewGuid(), Guid.NewGuid());
        await MarkPaymentActionRequiredAsync(invalidOrderId);
        await CancelForStockRejectionAsync(ineligibleOrderId);

        var invalid = await SendRetryAsync(invalidOrderId, string.Empty);
        var (ineligible, tracking) = await SendRetryWithTrackingAsync(ineligibleOrderId, "retry-ineligible");

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, ineligible.StatusCode);
        Assert.False((await ReadOrderAsync(invalidOrderId)).HasRecordedRetryRequest(string.Empty));
        Assert.False((await ReadOrderAsync(ineligibleOrderId)).HasRecordedRetryRequest("retry-ineligible"));
        Assert.Empty(tracking.Sent.MessagesOf<PaymentRetryRequestedIntegrationEvent>());
    }

    [Fact]
    public async Task RetryPaymentHandler_CrossTenantOrder_IsFilteredWithoutRetry()
    {
        const string otherTenantId = "00000000-0000-0000-0000-000000000002";
        var orderId = await DeliverV2CheckoutAsync(Guid.NewGuid(), Guid.NewGuid(), tenantId: otherTenantId);
        await MarkPaymentActionRequiredAsync(orderId);

        var result = await InvokeTenantScopedRetryAsync(orderId, "retry-cross-tenant", MockBearerAuthenticationHandler.TestTenantId);
        var order = await ReadOrderAsync(orderId);

        Assert.True(result.IsError);
        Assert.Equal(otherTenantId, order.TenantId);
        Assert.Equal(PaymentState.ActionRequired, order.PaymentState);
        Assert.False(order.HasRecordedRetryRequest("retry-cross-tenant"));
    }

    private async Task<Guid> DeliverV2CheckoutAsync(Guid customerId, Guid productId, string tenantId = MockBearerAuthenticationHandler.TestTenantId)
    {
        var evt = new BasketCheckedOutV2IntegrationEvent
        {
            BasketId = Guid.NewGuid(),
            CustomerId = customerId,
            KeycloakSubjectId = "subject-test-user",
            TenantId = tenantId,
            Amount = 37.5m,
            AuthorizedAmount = 40m,
            Currency = "USD",
            PaymentMethodToken = "pm_test_token",
            SourceCorrelationId = Guid.NewGuid().ToString("N"),
            CheckedOutAt = DateTimeOffset.UtcNow,
            Items = [new BasketCheckedOutLineV2 { ProductId = productId, ProductName = "Test Product", Quantity = 3, UnitPrice = 12.5m, LineTotal = 37.5m }],
        };
        using var scope = Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await BasketCheckedOutV2Handler.Handle(evt, bus, CancellationToken.None);

        using var readScope = Services.CreateScope();
        var context = readScope.ServiceProvider.GetRequiredService<Orders.Application.Database.OrderDbContext>();
        var order = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(context.Orders, order => order.CheckoutCorrelationId == evt.SourceCorrelationId);
        return order.Id;
    }

    private async Task MarkPaymentActionRequiredAsync(Guid orderId)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<Orders.Application.Database.OrderDbContext>();
        var order = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(context.Orders.IgnoreQueryFilters(), candidate => candidate.Id == orderId);
        Assert.IsType<Orders.Domain.DomainEvents.OrderPaymentActionRequired>(order.ApplyPaymentFailure("generic-decline", "Use another method.", $"payment-failed:{orderId:N}", order.CheckoutCorrelationId));
        await context.SaveChangesAsync();
    }

    private async Task<Order> ReadOrderAsync(Guid orderId)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<Orders.Application.Database.OrderDbContext>();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(context.Orders.IgnoreQueryFilters(), candidate => candidate.Id == orderId);
    }

    private async Task CancelForStockRejectionAsync(Guid orderId)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<Orders.Application.Database.OrderDbContext>();
        var order = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(context.Orders.IgnoreQueryFilters(), candidate => candidate.Id == orderId);
        Assert.NotNull(order.ApplyStockRejected($"stock-rejected:{orderId:N}", order.CheckoutCorrelationId, "The requested item is unavailable."));
        await context.SaveChangesAsync();
    }

    private async Task<HttpResponseMessage> SendRetryAsync(Guid orderId, string requestId, string? subject = null, bool omitSubject = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/orders/{orderId}/payment-retry")
        {
            Content = JsonContent.Create(new { requestId, paymentMethodToken = "token-replacement" }),
        };
        if (omitSubject)
        {
            request.Headers.Add("X-Test-Omit-Subject", "true");
        }
        else if (subject is not null)
        {
            request.Headers.Add("X-Test-Subject", subject);
        }

        return await Client.SendAsync(request);
    }

    private async Task<(HttpResponseMessage Response, ITrackedSession Tracking)> SendRetryWithTrackingAsync(Guid orderId, string requestId, string? subject = null, bool omitSubject = false)
    {
        HttpResponseMessage? response = null;
        Func<IMessageContext, Task> send = async _ =>
        {
            response = await SendRetryAsync(orderId, requestId, subject, omitSubject).ConfigureAwait(false);
        };
        var tracking = await Services.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(10))
            .ExecuteAndWaitAsync(send)
            .ConfigureAwait(false);

        return (response!, tracking);
    }

    private async Task<ErrorOr<Success>> InvokeTenantScopedRetryAsync(Guid orderId, string requestId, string tenantId)
    {
        using var scope = Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IMultiTenantContextAccessor<TenantDetails>>();
        var setter = scope.ServiceProvider.GetRequiredService<IMultiTenantContextSetter>();
        var previous = accessor.MultiTenantContext;
        setter.MultiTenantContext = new MultiTenantContext<TenantDetails>(new TenantDetails
        {
            Id = tenantId,
            Identifier = tenantId,
            Name = tenantId,
            IsActive = true,
        });

        try
        {
            var context = scope.ServiceProvider.GetRequiredService<Orders.Application.Database.OrderDbContext>();
            Assert.Equal(tenantId, context.TenantId);

            var orders = scope.ServiceProvider.GetRequiredService<SharedKernel.Core.Database.IGenericWriteRepository<Order, Guid>>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<SharedKernel.Core.Database.IUnitOfWork>();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            var httpContext = new DefaultHttpContext();
            httpContext.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("sub", "subject-test-user")], MockBearerAuthenticationHandler.SchemeName));
            var identity = new OrderIdentityAccessor(new HttpContextAccessor { HttpContext = httpContext });

            return await RetryPaymentHandler.Handle(
                new RetryPaymentCommand(orderId, requestId, "token-replacement"),
                orders,
                identity,
                context.TenantDetails!,
                unitOfWork,
                bus,
                CancellationToken.None);
        }
        finally
        {
            setter.MultiTenantContext = previous;
        }
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
        databaseConnectionString = fixture
            .CreateSharedTestDatabaseAsync(
                typeof(Orders.Application.Database.OrderDbContext),
                "Order.Host")
            .GetAwaiter()
            .GetResult();

        factory = new OrderWebApplicationFactory(fixture, databaseConnectionString);
        Client = factory.CreateClient();
    }

    protected HttpClient Client { get; }

    protected IServiceProvider Services => factory.Services;

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
        static OrderWebApplicationFactory() => JasperFxEnvironment.AutoStartHost = true;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:OrderWrite", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:OrderRead", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:Default", databaseConnectionString);
            builder.UseSetting("Keycloak:realm", "test");
            builder.UseSetting("Keycloak:auth-server-url", "http://localhost:8080");
            builder.UseSetting("Keycloak:resource", "order-api");
            builder.ConfigureTestServices(services =>
            {
                services.AddMultiTenant<TenantDetails>();
                services.AddScoped<ITenantInfo>(serviceProvider =>
                {
                    var tenantId = serviceProvider.GetRequiredService<IHttpContextAccessor>()
                        .HttpContext?
                        .Request
                        .Headers["X-Test-TenantId"]
                        .FirstOrDefault()
                        ?? MockBearerAuthenticationHandler.TestTenantId;
                    return new TenantDetails
                    {
                        Id = tenantId,
                        Identifier = tenantId,
                        Name = tenantId,
                        IsActive = true,
                    };
                });
                services.AddTransient<MockBearerAuthenticationHandler>();
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    var bearerScheme = options.Schemes.FirstOrDefault(scheme => scheme.Name == MockBearerAuthenticationHandler.SchemeName);
                    if (bearerScheme is not null)
                    {
                        bearerScheme.HandlerType = typeof(MockBearerAuthenticationHandler);
                    }

                    options.DefaultAuthenticateScheme = MockBearerAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = MockBearerAuthenticationHandler.SchemeName;
                    options.DefaultForbidScheme = MockBearerAuthenticationHandler.SchemeName;
                });
                var keycloakHandlerDescriptor = services.FirstOrDefault(descriptor => descriptor.ImplementationType?.Name == "ParameterizedProtectedResourceRequirementHandler");
                if (keycloakHandlerDescriptor is not null)
                {
                    services.Remove(keycloakHandlerDescriptor);
                }

                services.AddSingleton<IAuthorizationHandler, PermissiveProtectedResourceHandler>();
            });
        }
    }

    private sealed class PermissiveProtectedResourceHandler : AuthorizationHandler<ParameterizedProtectedResourceRequirement>
    {
        /// <inheritdoc/>
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ParameterizedProtectedResourceRequirement requirement)
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}

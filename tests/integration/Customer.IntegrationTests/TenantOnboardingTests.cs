using System.Net.Http.Headers;
using System.Net;
using System.Net.Http.Json;
using Customers.Application.Database;
using Customers.Host.Endpoints.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Customers.IntegrationTests;

/// <summary>Exercises authenticated pending tenant reservations at the HTTP boundary.</summary>
[Collection("SharedTestcontainers")]
public sealed class TenantOnboardingTests(SharedTestcontainersFixture fixture) : CustomerIntegrationTestBase(fixture)
{

    [Fact]
    public async Task PurchaseReservationEndpoint_RejectsUntrustedCallers()
    {
        var tenant = Customers.Domain.Entities.Tenant.CreatePendingReservation(Guid.NewGuid(), $"purchase-{Guid.NewGuid():N}", MockBearerAuthenticationHandler.TestSubject);
        using var scope = CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        using var response = await Client.GetAsync($"/internal/purchase-reservations/{tenant.Id:D}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PurchaseReservationEndpoint_ReturnsCanonicalOwnerOnlyForBillingClient()
    {
        using var scope = CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
        var tenant = Customers.Domain.Entities.Tenant.CreatePendingReservation(Guid.NewGuid(), $"reserved-{Guid.NewGuid():N}", MockBearerAuthenticationHandler.TestSubject);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        using var anonymous = await Client.GetAsync($"/internal/purchase-reservations/{tenant.Id:D}");
        Assert.Equal(HttpStatusCode.Forbidden, anonymous.StatusCode);

        using var authenticated = await SendWithBillingClientAsync($"/internal/purchase-reservations/{tenant.Id:D}");
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
        var reservation = await authenticated.Content.ReadFromJsonAsync<PurchaseReservationResponse>();
        Assert.NotNull(reservation);
        Assert.Equal(tenant.Id, reservation!.TenantId);
        Assert.Equal(tenant.Identifier, reservation.TenantIdentifier);
        Assert.Equal(tenant.OwnerSubjectId, reservation.OwnerSubjectId);
    }

    private async Task<HttpResponseMessage> SendWithBillingClientAsync(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "billing-test");
        return await Client.SendAsync(request);
    }
    [Fact]
    public async Task Onboarding_IgnoresForgedPurchaseFieldsAndReturnsOnlyOwnerReservation()
    {
        using var response = await Client.PostAsJsonAsync("/tenants/onboarding", new
        {
            DisplayName = "Red test", TenantIdentifier = "forged-slug",
            PlanId = "forged", PlanVersion = "forged", PurchaseOrderId = Guid.NewGuid(),
            DatabaseStrategy = "attacker-controlled", DatabaseProvider = "unknown", HasReadReplicas = true,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TenantOnboardingDto>();
        Assert.NotNull(body);
        Assert.Equal("pending", body!.Status);
        Assert.Equal(MockBearerAuthenticationHandler.TestSubject, body.OwnerSubjectId);
        Assert.NotEqual(Guid.Empty, body.TenantId);
        using var scope = CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
        var persisted = await db.Tenants.SingleAsync(item => item.Identifier == body.Identifier);
        Assert.Equal(MockBearerAuthenticationHandler.TestSubject, persisted.OwnerSubjectId);
        Assert.Equal(string.Empty, persisted.PlanId);
        Assert.Equal(Guid.Empty, persisted.PurchaseOrderId);
        Assert.Equal(string.Empty, persisted.DatabaseStrategy);
        Assert.Equal(string.Empty, persisted.DatabaseProvider);
        Assert.False(persisted.HasReadReplicas);
    }

    [Fact]
    public async Task Onboarding_RejectsDisplayNameThatCannotProduceSlug()
    {
        using var response = await Client.PostAsJsonAsync("/tenants/onboarding", new { DisplayName = "!!!" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Onboarding_RetryReturnsSameReservationForSameOwner()
    {
        using var firstResponse = await Client.PostAsJsonAsync("/tenants/onboarding", new { DisplayName = "Retry business" });
        using var retryResponse = await Client.PostAsJsonAsync("/tenants/onboarding", new { DisplayName = "Retry business" });
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        var first = await firstResponse.Content.ReadFromJsonAsync<TenantOnboardingDto>();
        var retry = await retryResponse.Content.ReadFromJsonAsync<TenantOnboardingDto>();
        Assert.NotNull(first);
        Assert.NotNull(retry);
        Assert.Equal(first!.TenantId, retry!.TenantId);
    }

    [Fact]
    public async Task Onboarding_DoesNotDiscloseTenantSlugOwnedByAnotherSubject()
    {
        const string slug = "reservation-owner-red-test";
        var other = Customers.Domain.Entities.Tenant.CreatePendingReservation(Guid.NewGuid(), slug, "different-keycloak-subject");
        await SeedTenantAsync(other);

        using var response = await Client.PostAsJsonAsync("/tenants/onboarding", new { DisplayName = "Foreign", TenantIdentifier = slug });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}

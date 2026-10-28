using System.Net;
using System.Net.Http.Json;
using Billings.Application.Database;
using Billings.Domain.Entities;
using Billings.Host.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Core.Pricing;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Billing.IntegrationTests;

/// <summary>Exercises owner-bound renewal quoting through the actual Billing HTTP host.</summary>
[Collection("SharedTestcontainers")]
public sealed class PurchaseRenewalQuoteHttpTests : BillingIntegrationTestBase
{
    private readonly ReservationHandler reservation;

    public PurchaseRenewalQuoteHttpTests(SharedTestcontainersFixture fixture)
        : this(fixture, new ReservationHandler())
    {
    }

    private PurchaseRenewalQuoteHttpTests(SharedTestcontainersFixture fixture, ReservationHandler reservation)
        : base(fixture, reservationHandler: reservation)
    {
        this.reservation = reservation;
    }

    [Fact]
    public async Task Renewal_RequiresVerifiedOwnerAndCurrentPaidReservation_ThenReturnsOnePendingServerPricedOrder()
    {
        var tenantId = Guid.NewGuid();
        var original = TenantPurchaseOrder.Create(tenantId, $"renew-{tenantId:N}",
            TenantPlan.Business, "2026-10",
            DatabaseStrategy.Dedicated, "postgres", false, 0,
            new Dictionary<string, decimal>(), DateTimeOffset.UtcNow, "buyer", "buyer");
        original.ConfirmCapturedPayment(Guid.NewGuid(), DateTimeOffset.UtcNow);
        await using (var scope = Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            database.TenantPurchaseOrders.Add(original);
            await database.SaveChangesAsync();
        }

        reservation.TenantId = tenantId;
        reservation.TenantIdentifier = original.TenantIdentifier;
        reservation.PurchaseOrderId = original.Id;
        reservation.Status = "active";

        using (var intruder = Request(tenantId, "intruder"))
        using (var response = await Client.SendAsync(intruder))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        reservation.PurchaseOrderId = Guid.NewGuid();
        using (var forged = Request(tenantId, "buyer"))
        using (var response = await Client.SendAsync(forged))
            Assert.NotEqual(HttpStatusCode.Created, response.StatusCode);

        reservation.PurchaseOrderId = original.Id;
        using var authorized = Request(tenantId, "buyer");
        using var created = await Client.SendAsync(authorized);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var quoted = await created.Content.ReadFromJsonAsync<QuoteResult>();
        Assert.NotNull(quoted);
        Assert.NotEqual(original.Id, quoted.Id);
        Assert.Equal(14999, quoted.AmountMinor);
        Assert.Equal("pending", quoted.Status);

        using var repeat = Request(tenantId, "buyer");
        using var retried = await Client.SendAsync(repeat);
        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        Assert.Equal(quoted.Id, (await retried.Content.ReadFromJsonAsync<QuoteResult>())?.Id);
        await using var verify = Services.CreateAsyncScope();
        Assert.Equal(2, await verify.ServiceProvider.GetRequiredService<BillingDbContext>()
            .TenantPurchaseOrders.CountAsync(item => item.TenantId == tenantId));
    }

    private static HttpRequestMessage Request(Guid tenantId, string buyer)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/purchases/renewals")
        {
            Content = JsonContent.Create(new
            {
                ReservationId = tenantId,
                PlanId = "Business",
                PlanVersion = "2026-10",
                DatabaseProvider = "postgres",
            }),
        };
        request.Headers.Add("X-Test-Onboarding-Subject", buyer);
        return request;
    }

    private sealed class ReservationHandler : HttpMessageHandler
    {
        public Guid TenantId { get; set; }
        public string TenantIdentifier { get; set; } = string.Empty;
        public Guid PurchaseOrderId { get; set; }
        public string Status { get; set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal)
                    ? JsonContent.Create(new { access_token = "service-token" })
                    : JsonContent.Create(new PurchaseReservationResponse
                    {
                        TenantId = TenantId,
                        TenantIdentifier = TenantIdentifier,
                        OwnerSubjectId = "buyer",
                        Status = Status,
                        PurchaseOrderId = PurchaseOrderId,
                    }),
            });
    }

    private sealed record QuoteResult(Guid Id, long AmountMinor, string Status);
}

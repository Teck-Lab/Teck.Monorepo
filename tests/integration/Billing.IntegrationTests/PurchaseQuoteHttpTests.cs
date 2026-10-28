using System.Net;
using System.Net.Http.Json;
using Billings.Host.Infrastructure;
using Teck.Platform.IntegrationTests.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Billing.IntegrationTests;

/// <summary>Exercises buyer and sales quote authorization before a tenant claim exists.</summary>
[Collection("SharedTestcontainers")]
public sealed class PurchaseQuoteHttpTests(SharedTestcontainersFixture fixture)
    : BillingIntegrationTestBase(fixture, reservationHandler: new ReservationHandler())
{
    private static readonly Guid OwnedReservationId = Guid.Parse("7eb658d5-6bc9-4777-9dc3-0751261842dc");
    private static readonly Guid ForeignReservationId = Guid.Parse("29e5cc83-2322-451e-99b9-99c9b3fbb904");

    [Fact]
    public async Task Quote_WithOnlyBuyerSubjectAndOwnedReservation_ReturnsServerPricedOrder()
    {
        using var request = CreateRequest(OwnedReservationId, "buyer");
        using var response = await Client.SendAsync(request).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<QuoteResult>().ConfigureAwait(false);
        Assert.NotNull(order);
        Assert.Equal(OwnedReservationId, order.TenantId);
        Assert.Equal("owned-business", order.TenantIdentifier);
        Assert.Equal(14999, order.AmountMinor);
        Assert.Equal("pending", order.Status);
    }

    [Fact]
    public async Task Status_WithOnlyBuyerSubject_ReportsPendingQuoteWithoutTenantClaims()
    {
        using var request = CreateRequest(OwnedReservationId, "buyer");
        using var created = await Client.SendAsync(request).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var original = await created.Content.ReadFromJsonAsync<QuoteResult>().ConfigureAwait(false);
        Assert.NotNull(original);

        using var statusRequest = new HttpRequestMessage(HttpMethod.Get, $"/purchases/{OwnedReservationId:D}");
        statusRequest.Headers.Add("X-Test-Onboarding-Subject", "buyer");
        using var response = await Client.SendAsync(statusRequest).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<QuoteResult>().ConfigureAwait(false);
        Assert.NotNull(status);
        Assert.Equal(original.Id, status.Id);
        Assert.Equal("pending", status.Status);

        using var foreignRequest = new HttpRequestMessage(HttpMethod.Get, $"/purchases/{OwnedReservationId:D}");
        foreignRequest.Headers.Add("X-Test-Onboarding-Subject", "other-buyer");
        using var foreign = await Client.SendAsync(foreignRequest).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task PendingQuote_BuyerCanCreateStripeIntent_ForeignSubjectCannot()
    {
        using var quoteRequest = CreateRequest(OwnedReservationId, "buyer");
        using var quoted = await Client.SendAsync(quoteRequest).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, quoted.StatusCode);
        var order = await quoted.Content.ReadFromJsonAsync<QuoteResult>().ConfigureAwait(false);
        Assert.NotNull(order);

        using var foreignRequest = new HttpRequestMessage(HttpMethod.Post, $"/purchases/{order.Id:D}/payment-intent");
        foreignRequest.Headers.Add("X-Test-Onboarding-Subject", "other-buyer");
        using var denied = await Client.SendAsync(foreignRequest).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        using var ownerRequest = new HttpRequestMessage(HttpMethod.Post, $"/purchases/{order.Id:D}/payment-intent");
        ownerRequest.Headers.Add("X-Test-Onboarding-Subject", "buyer");
        using var intent = await Client.SendAsync(ownerRequest).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, intent.StatusCode);
        var payment = await intent.Content.ReadFromJsonAsync<StripeIntentResult>().ConfigureAwait(false);
        Assert.NotNull(payment);
        Assert.Equal(order.Id, payment.OrderId);
        Assert.StartsWith("pi_", payment.PaymentIntentId, StringComparison.Ordinal);
        Assert.Equal(order.AmountMinor, StripePurchases.CreatedAmountMinor);
        Assert.Equal("USD", StripePurchases.CreatedCurrency);
        Assert.Equal(1, StripePurchases.CreateCount);
        using var retryRequest = new HttpRequestMessage(HttpMethod.Post, $"/purchases/{order.Id:D}/payment-intent");
        retryRequest.Headers.Add("X-Test-Onboarding-Subject", "buyer");
        using var retried = await Client.SendAsync(retryRequest).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        var sameIntent = await retried.Content.ReadFromJsonAsync<StripeIntentResult>().ConfigureAwait(false);
        Assert.Equal(payment, sameIntent);
        Assert.Equal(1, StripePurchases.CreateCount);
        Assert.NotEmpty(payment.ClientSecret);
    }

    [Fact]
    public async Task Quote_WithForeignReservationOrUnidentifiedSalesBeneficiary_IsForbidden()
    {
        using var foreign = CreateRequest(ForeignReservationId, "buyer");
        using var foreignResponse = await Client.SendAsync(foreign).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, foreignResponse.StatusCode);

        using var sales = CreateRequest(ForeignReservationId, "sales-actor", salesRole: true);
        using var salesResponse = await Client.SendAsync(sales).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, salesResponse.StatusCode);
    }

    [Fact]
    public async Task Quote_WithVerifiedSalesBeneficiary_AuditsActorAndOwner()
    {
        using var request = CreateRequest(ForeignReservationId, "sales-actor", salesRole: true, beneficiary: "other-buyer");
        using var response = await Client.SendAsync(request).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<QuoteResult>().ConfigureAwait(false);
        Assert.NotNull(order);
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Billings.Application.Database.BillingDbContext>();
        var persisted = await db.TenantPurchaseOrders.SingleAsync(item => item.Id == order.Id).ConfigureAwait(false);
        Assert.Equal("sales-actor", persisted.CreatedBySubjectId);
        Assert.Equal("other-buyer", persisted.BeneficiarySubjectId);
    }

    private static HttpRequestMessage CreateRequest(Guid reservationId, string subject, bool salesRole = false, string? beneficiary = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/purchases");
        request.Headers.Add("X-Test-Onboarding-Subject", subject);
        if (salesRole)
        {
            request.Headers.Add("X-Test-Sales-Role", "true");
        }

        request.Content = JsonContent.Create(new
        {
            ReservationId = reservationId,
            BeneficiarySubjectId = beneficiary,
            PlanId = "Business",
            PlanVersion = "2026-10",
            DatabaseProvider = "postgres",
        });
        return request;
    }

    private sealed class ReservationHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { access_token = "service-token" }),
                });
            }

            var id = request.RequestUri.AbsolutePath.Split('/').Last();
            var reservationId = Guid.Parse(id);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new PurchaseReservationResponse
                {
                    TenantId = reservationId,
                    TenantIdentifier = reservationId == OwnedReservationId ? "owned-business" : "foreign-business",
                    OwnerSubjectId = reservationId == OwnedReservationId ? "buyer" : "other-buyer",
                    Status = "pending",
                }),
            });
        }
    }

    private sealed record StripeIntentResult(Guid OrderId, string PaymentIntentId, string ClientSecret);

    private sealed record QuoteResult(Guid Id, Guid TenantId, string TenantIdentifier, long AmountMinor, string Status);
}

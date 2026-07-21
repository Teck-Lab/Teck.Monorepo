// <copyright file="PaymentFlowTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Http.Json;
using Billings.Application.Billing.Payments.Responses;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Billing.IntegrationTests;

/// <summary>
/// End-to-end tests for the payment capture flow: boots Billing.Host over a Testcontainers
/// Postgres database and exercises the real HTTP endpoints, proving the capture handler, the
/// stub payment provider, EF persistence and the idempotency guard all work together.
/// </summary>
[Collection("SharedTestcontainers")]
public sealed class PaymentFlowTests : BillingIntegrationTestBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PaymentFlowTests"/> class.
    /// </summary>
    /// <param name="fixture">The shared testcontainers fixture providing Postgres.</param>
    public PaymentFlowTests(SharedTestcontainersFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task CapturePayment_ReturnsCreated_Captured()
    {
        var response = await Client.PostAsJsonAsync(
            "/payments",
            new
            {
                OrderId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                Amount = 42.50m,
                Currency = "USD",
            });

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payment = await response.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.NotNull(payment);
        Assert.NotEqual(Guid.Empty, payment!.Id);
        Assert.Equal("Captured", payment.Status);
        Assert.NotNull(payment.ProviderReference);
        Assert.NotEmpty(payment.ProviderReference!);
    }

    [Fact]
    public async Task GetPayment_AfterCapture_ReturnsPayment()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var captureResponse = await Client.PostAsJsonAsync(
            "/payments",
            new { OrderId = orderId, CustomerId = customerId, Amount = 15m, Currency = "USD" });
        captureResponse.EnsureSuccessStatusCode();
        var captured = await captureResponse.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.NotNull(captured);

        var getResponse = await Client.GetAsync($"/payments/{captured!.Id}");
        var getBody = await getResponse.Content.ReadAsStringAsync();
        Assert.True(getResponse.IsSuccessStatusCode, $"GET /payments/{captured.Id} failed: {(int)getResponse.StatusCode} {getBody}");

        var fetched = await getResponse.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.NotNull(fetched);
        Assert.Equal(captured.Id, fetched!.Id);
        Assert.Equal(orderId, fetched.OrderId);
    }

    [Fact]
    public async Task ListPayments_AfterCapture_IncludesPayment()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var captureResponse = await Client.PostAsJsonAsync(
            "/payments",
            new { OrderId = orderId, CustomerId = customerId, Amount = 7.25m, Currency = "USD" });
        captureResponse.EnsureSuccessStatusCode();
        var captured = await captureResponse.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.NotNull(captured);

        var listResponse = await Client.GetAsync("/payments");
        var listBody = await listResponse.Content.ReadAsStringAsync();
        Assert.True(listResponse.IsSuccessStatusCode, $"GET /payments failed: {(int)listResponse.StatusCode} {listBody}");

        var payments = await listResponse.Content.ReadFromJsonAsync<IReadOnlyList<PaymentDto>>();
        Assert.NotNull(payments);
        Assert.Contains(payments!, payment => payment.Id == captured!.Id);
    }

    [Fact]
    public async Task CapturePayment_SameOrderTwice_IsIdempotent()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var request = new { OrderId = orderId, CustomerId = customerId, Amount = 99.99m, Currency = "USD" };

        var firstResponse = await Client.PostAsJsonAsync("/payments", request);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var first = await firstResponse.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.NotNull(first);

        // Replaying the same order must never charge it twice — the handler's idempotency guard
        // (PaymentByOrderSpec lookup before creating a new Payment) must return the existing
        // payment rather than throwing on the unique OrderId index or minting a duplicate.
        var secondResponse = await Client.PostAsJsonAsync("/payments", request);
        var secondBody = await secondResponse.Content.ReadAsStringAsync();
        Assert.True(secondResponse.IsSuccessStatusCode, $"Second POST /payments failed: {(int)secondResponse.StatusCode} {secondBody}");

        var second = await secondResponse.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.NotNull(second);
        Assert.Equal(first!.Id, second!.Id);
    }
}

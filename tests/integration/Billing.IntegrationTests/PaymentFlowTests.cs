// <copyright file="PaymentFlowTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Net.Http.Json;
using Billings.Application.Billing.Payments.Responses;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Events;
using Teck.Platform.IntegrationTests.Shared;
using Wolverine;
using Xunit;

namespace Billing.IntegrationTests;

/// <summary>Exercises V2 lifecycle-created payments through the real Billing consumer.</summary>
[Collection("SharedTestcontainers")]
public sealed class PaymentFlowTests : BillingIntegrationTestBase
{
    /// <summary>Initializes a new payment flow test.</summary>
    /// <param name="fixture">The shared PostgreSQL fixture.</param>
    public PaymentFlowTests(SharedTestcontainersFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task CapturePayment_ReturnsCreated_Captured()
    {
        var payment = await DeliverOrderAsync(Guid.NewGuid());

        Assert.NotEqual(Guid.Empty, payment.Id);
        Assert.Equal("Captured", payment.Status);
        Assert.NotNull(payment.ProviderReference);
    }

    [Fact]
    public async Task GetPayment_AfterCapture_ReturnsPayment()
    {
        var orderId = Guid.NewGuid();
        var captured = await DeliverOrderAsync(orderId);

        var response = await Client.GetAsync($"/payments/{captured.Id}");
        response.EnsureSuccessStatusCode();
        var fetched = await response.Content.ReadFromJsonAsync<PaymentDto>();

        Assert.NotNull(fetched);
        Assert.Equal(captured.Id, fetched!.Id);
        Assert.Equal(orderId, fetched.OrderId);
    }

    [Fact]
    public async Task ListPayments_AfterCapture_IncludesPayment()
    {
        var captured = await DeliverOrderAsync(Guid.NewGuid());

        var response = await Client.GetAsync("/payments");
        response.EnsureSuccessStatusCode();
        var payments = await response.Content.ReadFromJsonAsync<IReadOnlyList<PaymentDto>>();

        Assert.NotNull(payments);
        Assert.Contains(payments!, payment => payment.Id == captured.Id);
    }

    [Fact]
    public async Task CapturePayment_SameOrderTwice_IsIdempotent()
    {
        var orderId = Guid.NewGuid();
        var requestId = $"request-{Guid.NewGuid():N}";
        var first = await DeliverOrderAsync(orderId, requestId);
        var second = await DeliverOrderAsync(orderId, requestId);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, Provider.AttemptCalls);
    }

    [Theory]
    [InlineData("missing-authorization")]
    [InlineData("non-positive-authorization")]
    [InlineData("blank-token")]
    [InlineData("over-limit-token")]
    [InlineData("blank-request")]
    [InlineData("over-limit-request")]
    [InlineData("invalid-currency")]
    [InlineData("over-scale-amount")]
    [InlineData("over-scale-authorization")]
    [InlineData("over-ceiling")]
    public async Task CapturePayment_InvalidV2Authority_DoesNotPersistPayment(string invalidInput)
    {
        var evt = CreateOrderPlacedEvent(Guid.NewGuid());
        ApplyInvalidInput(evt, invalidInput);

        using var scope = Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await bus.InvokeAsync(evt, CancellationToken.None);

        Assert.Equal(0, Provider.AttemptCalls);
        Assert.Equal(0, Provider.CaptureCalls);

        var response = await Client.GetAsync("/payments");
        response.EnsureSuccessStatusCode();
        var payments = await response.Content.ReadFromJsonAsync<IReadOnlyList<PaymentDto>>();

        Assert.Empty(payments ?? []);
    }

    private async Task<PaymentDto> DeliverOrderAsync(Guid orderId, string? requestId = null)
    {
        var evt = CreateOrderPlacedEvent(orderId, requestId);

        using var scope = Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await bus.InvokeAsync(evt, CancellationToken.None);

        for (var attempt = 0; attempt < 50; attempt++)
        {
            var response = await Client.GetAsync("/payments");
            response.EnsureSuccessStatusCode();
            var payments = await response.Content.ReadFromJsonAsync<IReadOnlyList<PaymentDto>>();
            var payment = payments?.SingleOrDefault(candidate => candidate.OrderId == orderId);
            if (payment is not null)
            {
                return payment;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new Xunit.Sdk.XunitException($"Payment for order {orderId} was not persisted after lifecycle delivery.");
    }

    private static OrderPlacedV2IntegrationEvent CreateOrderPlacedEvent(Guid orderId, string? requestId = null) =>
        new()
        {
            OrderId = orderId,
            BasketId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            KeycloakSubjectId = "test-user",
            TenantId = MockBearerAuthenticationHandler.TestTenantId,
            Amount = 42.50m,
            AuthorizedAmount = 50m,
            Currency = "USD",
            PaymentMethodToken = "pm_test_token",
            RequestId = requestId ?? $"request-{Guid.NewGuid():N}",
            SourceCorrelationId = $"correlation-{Guid.NewGuid():N}",
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static void ApplyInvalidInput(OrderPlacedV2IntegrationEvent evt, string invalidInput)
    {
        switch (invalidInput)
        {
            case "missing-authorization": evt.AuthorizedAmount = 0m; break;
            case "non-positive-authorization": evt.AuthorizedAmount = -1m; break;
            case "blank-token": evt.PaymentMethodToken = " "; break;
            case "over-limit-token": evt.PaymentMethodToken = new string('t', 257); break;
            case "blank-request": evt.RequestId = " "; break;
            case "over-limit-request": evt.RequestId = new string('r', 129); break;
            case "invalid-currency": evt.Currency = "usd"; break;
            case "over-scale-amount": evt.Amount = 42.501m; break;
            case "over-scale-authorization": evt.AuthorizedAmount = 50.001m; break;
            case "over-ceiling": evt.Amount = 50.01m; break;
            default: throw new ArgumentOutOfRangeException(nameof(invalidInput), invalidInput, null);
        }
    }
}

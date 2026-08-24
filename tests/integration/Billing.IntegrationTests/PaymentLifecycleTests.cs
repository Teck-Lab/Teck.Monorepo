// <copyright file="PaymentLifecycleTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using Billings.Application.Billing;
using Billings.Application.Billing.Payments.Features.CapturePayment.V1;
using Billings.Application.Billing.Payments.Features.ProcessPaymentOutcome.V1;
using Billings.Application.Billing.Payments.Features.RetryPayment.V1;
using Billings.Application.Billing.Payments.Responses;
using Billings.Application.Database;
using Billings.Domain.Entities;
using Billings.Domain.ValueObjects;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Events;
using SharedKernel.Infrastructure.MultiTenant;
using Teck.Platform.IntegrationTests.Shared;
using Wolverine;
using Wolverine.Tracking;
using Xunit;

namespace Billing.IntegrationTests;

/// <summary>
/// Exercises the complete V2 payment lifecycle through the real Billing host, PostgreSQL, and
/// Wolverine runtime while controlling only the external provider adapter.
/// </summary>
[Collection("SharedTestcontainers")]
public sealed class PaymentLifecycleTests : BillingIntegrationTestBase
{
    /// <summary>Initializes the lifecycle suite.</summary>
    /// <param name="fixture">The shared PostgreSQL fixture.</param>
    public PaymentLifecycleTests(SharedTestcontainersFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task PersistedAttemptBeforeOutcome_RedeliveredOutcomeConvergesWithoutDuplicateProviderCalls()
    {
        var orderId = Guid.NewGuid();
        var requestId = NewRequestId();
        Provider.QueueAttemptResults(RecordingPaymentProvider.Outcome("processing"));

        await DeliverOrderAsync(CreateOrder(orderId, requestId));
        await WaitForAsync(() => GetPaymentAsync(orderId), payment => payment?.Status == "Pending");

        var accepted = await WolverineHost.InvokeMessageAndWaitAsync(new ProcessPaymentOutcomeCommand(orderId, requestId, "succeeded", "delayed-reference", null));
        var duplicate = await WolverineHost.InvokeMessageAndWaitAsync(new ProcessPaymentOutcomeCommand(orderId, requestId, "succeeded", "delayed-reference", null));
        await DeliverOrderAsync(CreateOrder(orderId, requestId));

        var immediateOrderId = Guid.NewGuid();
        Provider.QueueAttemptResults(RecordingPaymentProvider.Outcome("succeeded"));
        await DeliverOrderAsync(CreateOrder(immediateOrderId, NewRequestId()));

        var payment = await WaitForAsync(() => GetPaymentAsync(orderId), candidate => candidate?.Status == "Captured");
        var immediatePayment = await WaitForAsync(() => GetPaymentAsync(immediateOrderId), candidate => candidate?.Status == "Captured");
        var persisted = await GetPersistedPaymentAsync(orderId);

        Assert.NotNull(payment);
        Assert.NotNull(immediatePayment);
        Assert.Equal("delayed-reference", payment!.ProviderReference);
        Assert.Single(accepted.NoRoutes.AllMessages().OfType<PaymentCapturedV2IntegrationEvent>());
        Assert.Empty(duplicate.NoRoutes.AllMessages().OfType<PaymentCapturedV2IntegrationEvent>());
        Assert.Equal(2, Provider.AttemptCalls);
        Assert.Single(persisted.Attempts);
        Assert.Single(await GetInvoicesAsync(orderId));
    }

    [Fact]
    public async Task ProviderAcceptedBeforeOutcomePersistence_RedeliveryUsesOnlyTheOriginalStableAttempt()
    {
        var orderId = Guid.NewGuid();
        var requestId = NewRequestId();
        Provider.QueueAttemptResults(
            RecordingPaymentProvider.Outcome("succeeded"),
            RecordingPaymentProvider.Outcome("succeeded"));
        Provider.InterruptAfterAcceptingNextAttempt();

        await DeliverOrderAsync(CreateOrder(orderId, requestId));
        var payment = await WaitForAsync(() => GetPaymentAsync(orderId), candidate => candidate?.Status == "Captured");
        var recovered = await GetPersistedPaymentAsync(orderId);

        Assert.NotNull(payment);
        Assert.Equal([requestId, requestId], Provider.AttemptRequests.Select(request => request.RequestId));
        Assert.Single(recovered.Attempts);
        Assert.Single(await GetInvoicesAsync(orderId));
    }

    [Fact]
    public async Task SameOrderWithDifferentRequestId_RemainsOnePaymentAttemptInvoiceAndProviderCall()
    {
        var orderId = Guid.NewGuid();
        var originalRequestId = NewRequestId();
        Provider.QueueAttemptResults(RecordingPaymentProvider.Outcome("succeeded"));

        var initial = await WolverineHost.InvokeMessageAndWaitAsync(CreateOrder(orderId, originalRequestId));
        var replay = await WolverineHost.InvokeMessageAndWaitAsync(CreateOrder(orderId, NewRequestId()));
        var payment = await WaitForAsync(() => GetPaymentAsync(orderId), candidate => candidate?.Status == "Captured");
        var persisted = await GetPersistedPaymentAsync(orderId);

        Assert.NotNull(payment);
        Assert.Equal(1, Provider.AttemptCalls);
        Assert.Equal([originalRequestId], Provider.AttemptRequests.Select(request => request.RequestId));
        Assert.Single(persisted.Attempts);
        Assert.Single(await GetInvoicesAsync(orderId));
        Assert.Single(initial.NoRoutes.AllMessages().OfType<PaymentCapturedV2IntegrationEvent>());
        Assert.Empty(replay.NoRoutes.AllMessages().OfType<PaymentCapturedV2IntegrationEvent>());
    }

    [Fact]
    public async Task TerminalCaptureAndReplay_SendExactlyOneCapturedLifecycleMessage()
    {
        var orderId = Guid.NewGuid();
        var requestId = NewRequestId();
        Provider.QueueAttemptResults(RecordingPaymentProvider.Outcome("succeeded"));

        var first = await WolverineHost.InvokeMessageAndWaitAsync(CreateOrder(orderId, requestId));
        var replay = await WolverineHost.InvokeMessageAndWaitAsync(CreateOrder(orderId, requestId));

        Assert.Single(first.NoRoutes.AllMessages().OfType<PaymentCapturedV2IntegrationEvent>());
        Assert.Empty(replay.NoRoutes.AllMessages().OfType<PaymentCapturedV2IntegrationEvent>());
        Assert.Equal(1, Provider.AttemptCalls);
        Assert.Single((await GetPersistedPaymentAsync(orderId)).Attempts);
        Assert.Single(await GetInvoicesAsync(orderId));
    }

    [Fact]
    public async Task MigratedTerminalV1PaymentWithoutAttempt_ReturnsExistingWithoutProviderPersistenceOrOutgoingSideEffects()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        await SeedTerminalLegacyPaymentWithoutAttemptAsync(orderId, customerId);

        var tracked = await WolverineHost.InvokeMessageAndWaitAsync(new CapturePaymentCommand(orderId, customerId, 42.50m, "USD"));
        var persisted = await GetPersistedPaymentAsync(orderId);

        Assert.Equal(PaymentStatus.Captured, persisted.Status);
        Assert.Empty(persisted.Attempts);
        Assert.Equal(0, Provider.AttemptCalls);
        Assert.Equal(0, Provider.CaptureCalls);
        Assert.Empty(await GetInvoicesAsync(orderId));
        Assert.Empty(tracked.NoRoutes.AllMessages().OfType<PaymentCapturedIntegrationEvent>());
        Assert.Empty(tracked.NoRoutes.AllMessages().OfType<PaymentFailedIntegrationEvent>());
        Assert.Empty(tracked.NoRoutes.AllMessages().OfType<PaymentCapturedV2IntegrationEvent>());
        Assert.Empty(tracked.NoRoutes.AllMessages().OfType<PaymentFailedV2IntegrationEvent>());
    }

    [Fact]
    public async Task DuplicatePendingRetry_ReusesTheStableProviderKeyWithoutCreatingAnotherAttempt()
    {
        var orderId = Guid.NewGuid();
        var requestId = NewRequestId();
        var retryRequestId = $"{requestId}-retry-1";
        Provider.QueueAttemptResults(
            RecordingPaymentProvider.Outcome("processing"),
            RecordingPaymentProvider.Outcome("processing"),
            RecordingPaymentProvider.Outcome("processing"));

        await DeliverOrderAsync(CreateOrder(orderId, requestId));
        await WaitForAsync(() => GetPaymentAsync(orderId), payment => payment?.Status == "Pending");

        var retry = new RetryPaymentCommand(orderId, 50m, "USD", "pm_lifecycle_token", retryRequestId, $"retry-{orderId:N}");
        var firstRetry = await WolverineHost.InvokeMessageAndWaitAsync(retry);
        var duplicateRetry = await WolverineHost.InvokeMessageAndWaitAsync(retry);

        var persisted = await GetPersistedPaymentAsync(orderId);
        Assert.Equal([requestId, retryRequestId, retryRequestId], Provider.AttemptRequests.Select(request => request.RequestId));
        Assert.Equal([1, 2], persisted.Attempts.OrderBy(attempt => attempt.AttemptNumber).Select(attempt => attempt.AttemptNumber));
        Assert.Empty(await GetInvoicesAsync(orderId));
        Assert.Empty(firstRetry.NoRoutes.AllMessages().OfType<PaymentCapturedV2IntegrationEvent>());
        Assert.Empty(duplicateRetry.NoRoutes.AllMessages().OfType<PaymentCapturedV2IntegrationEvent>());
    }

    [Fact]
    public async Task TransientRetries_SendNoIntermediateFailureAndExactlyOneAtPersistedExhaustion()
    {
        var orderId = Guid.NewGuid();
        var requestId = NewRequestId();
        SetPaymentProviderSetting("DeclineMappings:temporary", "transient");
        Provider.QueueAttemptResults(
            RecordingPaymentProvider.Outcome("failed", "temporary"),
            RecordingPaymentProvider.Outcome("failed", "temporary"),
            RecordingPaymentProvider.Outcome("failed", "temporary"));

        var tracked = await WolverineHost.InvokeMessageAndWaitAsync(CreateOrder(orderId, requestId));
        var payment = await WaitForAsync(() => GetPaymentAsync(orderId), candidate => candidate?.Status == "Failed" && Provider.AttemptCalls == 3);

        Assert.NotNull(payment);
        Assert.Single(tracked.NoRoutes.AllMessages().OfType<PaymentFailedV2IntegrationEvent>());
        Assert.Equal([requestId, $"{requestId}-retry-1", $"{requestId}-retry-2"], Provider.AttemptRequests.Select(request => request.RequestId));
        Assert.Equal([1, 2, 3], (await GetPersistedPaymentAsync(orderId)).Attempts.OrderBy(attempt => attempt.AttemptNumber).Select(attempt => attempt.AttemptNumber));
    }

    [Fact]
    public async Task TransientOutcomes_RetryWithStableKeysAndStopAtConfiguredExhaustion()
    {
        var orderId = Guid.NewGuid();
        var requestId = NewRequestId();
        SetPaymentProviderSetting("DeclineMappings:temporary", "transient");
        Provider.QueueAttemptResults(
            RecordingPaymentProvider.Outcome("failed", "temporary"),
            RecordingPaymentProvider.Outcome("failed", "temporary"),
            RecordingPaymentProvider.Outcome("failed", "temporary"));

        await DeliverOrderAsync(CreateOrder(orderId, requestId));
        var payment = await WaitForAsync(() => GetPaymentAsync(orderId), candidate => candidate?.Status == "Failed" && Provider.AttemptCalls == 3);
        var persisted = await GetPersistedPaymentAsync(orderId);

        Assert.NotNull(payment);
        Assert.Equal(3, Provider.AttemptCalls);
        Assert.Equal([requestId, $"{requestId}-retry-1", $"{requestId}-retry-2"], Provider.AttemptRequests.Select(request => request.RequestId));
        Assert.Equal([1, 2, 3], persisted.Attempts.OrderBy(attempt => attempt.AttemptNumber).Select(attempt => attempt.AttemptNumber));
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.Equal(3, Provider.AttemptCalls);
    }

    [Theory]
    [InlineData("requires_action")]
    [InlineData("requires_payment_method")]
    [InlineData("failed")]
    public async Task NonTransientOutcome_DoesNotScheduleAutomaticProviderRetry(string outcome)
    {
        var orderId = Guid.NewGuid();
        Provider.QueueAttemptResults(RecordingPaymentProvider.Outcome(outcome, "generic_decline"));

        await DeliverOrderAsync(CreateOrder(orderId, NewRequestId()));
        var payment = await WaitForAsync(() => GetPaymentAsync(orderId), candidate => candidate?.Status == "Failed");

        Assert.NotNull(payment);
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        Assert.Equal(1, Provider.AttemptCalls);
    }

    [Theory]
    [InlineData("fraudulent")]
    [InlineData("lost_card")]
    [InlineData("stolen_card")]
    [InlineData("block_list")]
    public async Task SensitiveProviderCodes_AreMaskedAsGenericDespiteHostConfiguration(string code)
    {
        var orderId = Guid.NewGuid();
        SetPaymentProviderSetting($"DeclineMappings:{code}", "transient");
        Provider.QueueAttemptResults(RecordingPaymentProvider.Outcome("failed", code));

        await DeliverOrderAsync(CreateOrder(orderId, NewRequestId()));
        var payment = await WaitForAsync(() => GetPaymentAsync(orderId), candidate => candidate?.Status == "Failed");
        var persisted = await GetPersistedPaymentAsync(orderId);

        Assert.NotNull(payment);
        Assert.Equal(DeclineCategory.GenericDecline, persisted.DeclineCategory);
        Assert.Equal(1, Provider.AttemptCalls);
    }

    [Fact]
    public async Task ReloadedDeclineMapping_IsAppliedByTheRunningHost()
    {
        const string code = "reloadable_issuer";
        SetPaymentProviderSetting($"DeclineMappings:{code}", "issuer-contact-required");
        var options = Services.GetRequiredService<IOptionsMonitor<PaymentProviderOptions>>();
        await WaitForAsync(
            () => Task.FromResult(options.CurrentValue.DeclineMappings.TryGetValue(code, out var value) ? value : null),
            value => value == "issuer-contact-required");

        var orderId = Guid.NewGuid();
        Provider.QueueAttemptResults(RecordingPaymentProvider.Outcome("failed", code));
        await DeliverOrderAsync(CreateOrder(orderId, NewRequestId()));
        await WaitForAsync(() => GetPaymentAsync(orderId), candidate => candidate?.Status == "Failed");

        var persisted = await GetPersistedPaymentAsync(orderId);
        Assert.Equal(DeclineCategory.IssuerContactRequired, persisted.DeclineCategory);
    }

    [Fact]
    public async Task TenantCollidingRequestKeys_ArePersistedAsIndependentAttempts()
    {
        var requestId = NewRequestId();
        var firstOrderId = Guid.NewGuid();
        var secondOrderId = Guid.NewGuid();
        Provider.QueueAttemptResults(RecordingPaymentProvider.Outcome("succeeded"), RecordingPaymentProvider.Outcome("succeeded"));

        await DeliverForTenantAsync("tenant-a", CreateOrder(firstOrderId, requestId));
        await DeliverForTenantAsync("tenant-b", CreateOrder(secondOrderId, requestId));

        await WaitForAsync(() => GetPersistedPaymentsAsync(firstOrderId, secondOrderId), payments => payments.Count == 2);
        var payments = await GetPersistedPaymentsAsync(firstOrderId, secondOrderId);

        Assert.Equal(2, Provider.AttemptCalls);
        Assert.Equal(["tenant-a", "tenant-b"], payments.Select(payment => payment.TenantId).Order());
        Assert.All(payments, payment => Assert.Single(payment.Attempts, attempt => attempt.RequestId == requestId));
    }

    [Theory]
    [InlineData("usd", 42.50, 50.00, "pm_test_token")]
    [InlineData("USD", 42.501, 50.00, "pm_test_token")]
    [InlineData("USD", 42.50, 50.001, "pm_test_token")]
    [InlineData("USD", 42.50, 50.00, " ")]
    public async Task InvalidLifecycleAuthorityBoundaries_HaveNoProviderOrDatabaseSideEffects(string currency, double amount, double authorizedAmount, string token)
    {
        await DeliverOrderAsync(CreateOrder(Guid.NewGuid(), NewRequestId(), (decimal)amount, (decimal)authorizedAmount, currency, token));

        Assert.Equal(0, Provider.AttemptCalls);
        Assert.Empty(await GetAllPaymentsAsync());
    }

    private async Task DeliverForTenantAsync(string tenantId, OrderPlacedV2IntegrationEvent evt)
    {
        using var scope = Services.CreateScope();
        var contextSetter = scope.ServiceProvider.GetRequiredService<IMultiTenantContextSetter>();
        contextSetter.MultiTenantContext = new MultiTenantContext<TenantDetails>(
            new TenantDetails { Id = tenantId, Identifier = tenantId });

        await scope.ServiceProvider.GetRequiredService<IMessageBus>().InvokeAsync(evt, CancellationToken.None);
    }

    private async Task DeliverOrderAsync(OrderPlacedV2IntegrationEvent evt) => await InvokeAsync(evt);

    private async Task InvokeAsync<TMessage>(TMessage message)
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMessageBus>().InvokeAsync(message, CancellationToken.None);
    }

    private static OrderPlacedV2IntegrationEvent CreateOrder(
        Guid orderId,
        string requestId,
        decimal amount = 42.50m,
        decimal authorizedAmount = 50m,
        string currency = "USD",
        string token = "pm_lifecycle_token") =>
        new()
        {
            OrderId = orderId,
            BasketId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            KeycloakSubjectId = "test-user",
            TenantId = MockBearerAuthenticationHandler.TestTenantId,
            Amount = amount,
            AuthorizedAmount = authorizedAmount,
            Currency = currency,
            PaymentMethodToken = token,
            RequestId = requestId,
            SourceCorrelationId = $"correlation-{Guid.NewGuid():N}",
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private async Task<PaymentDto?> GetPaymentAsync(Guid orderId)
    {
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        return await context.Payments
            .Where(payment => payment.OrderId == orderId)
            .Select(payment => new PaymentDto(payment.Id, payment.OrderId, payment.CustomerId, payment.Amount.Amount, payment.Amount.Currency, payment.Status.Name, payment.ProviderReference))
            .SingleOrDefaultAsync();
    }

    private async Task<Payment> GetPersistedPaymentAsync(Guid orderId)
    {
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        return await context.Payments.Include(payment => payment.Attempts).SingleAsync(payment => payment.OrderId == orderId);
    }

    private async Task<List<Payment>> GetPersistedPaymentsAsync(Guid firstOrderId, Guid secondOrderId)
    {
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        return await context.Payments.IgnoreQueryFilters().Include(payment => payment.Attempts)
            .Where(payment => payment.OrderId == firstOrderId || payment.OrderId == secondOrderId)
            .ToListAsync();
    }

    private async Task<List<Payment>> GetAllPaymentsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Payments.IgnoreQueryFilters().ToListAsync();
    }

    private async Task<List<Invoice>> GetInvoicesAsync(Guid orderId)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Invoices
            .Where(invoice => invoice.OrderId == orderId)
            .ToListAsync();
    }

    private async Task SeedTerminalLegacyPaymentWithoutAttemptAsync(Guid orderId, Guid customerId)
    {
        var paymentId = Guid.NewGuid();
        var legacyRequestId = $"legacy-{paymentId}";

        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO payments (
                "Id", "TenantId", "OrderId", "CustomerId", "Amount", "Currency", "Status", "ProviderReference", "CreatedAt", "IsDeleted",
                "AuthorizedAmount", "AuthorizedCurrency", "PaymentMethodToken", "RequestId", "SourceCorrelationId")
            VALUES (
                {paymentId}, {MockBearerAuthenticationHandler.TestTenantId}, {orderId}, {customerId}, {42.50m}, {"USD"}, {PaymentStatus.Captured.Value}, {"migrated-provider-reference"}, {DateTimeOffset.UtcNow}, {false},
                {42.50m}, {"USD"}, {"legacy-token"}, {legacyRequestId}, {string.Empty})
            """);
    }

    private static async Task<T> WaitForAsync<T>(Func<Task<T>> getValue, Func<T, bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var value = await getValue();
            if (condition(value))
            {
                return value;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new Xunit.Sdk.XunitException("The Billing lifecycle did not reach its expected persisted state.");
    }

    private static string NewRequestId() => $"lifecycle-{Guid.NewGuid():N}";
}

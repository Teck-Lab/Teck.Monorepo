// <copyright file="PaymentLifecycleTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Net.Http.Json;
using Billings.Application.Billing;
using Billings.Application.Billing.Invoices.Features.GetInvoice.V1;
using Billings.Application.Billing.Invoices.ReadModels;
using Billings.Application.Billing.Invoices.Responses;
using Billings.Application.Billing.Payments.Features.CapturePayment.V1;
using Billings.Application.Billing.Payments.Features.ProcessPaymentOutcome.V1;
using Billings.Application.Billing.Payments.Features.RetryPayment.V1;
using Billings.Application.Billing.Payments.Responses;
using Billings.Application.Database;
using Billings.Domain.Entities;
using Billings.Domain.ValueObjects;
using Billings.Host.Database;
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
    public async Task IncomingEnvelopeTenant_ResolvesLazilyInTheGeneratedHandlerPipeline()
    {
        const string tenantId = "unseeded-envelope-tenant";
        var orderId = Guid.NewGuid();
        await SeedPendingPaymentWithoutMessageTenantAsync(tenantId, orderId);
        Provider.QueueAttemptResults(RecordingPaymentProvider.Outcome("succeeded"));

        using var scope = Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        Assert.Null(scope.ServiceProvider.GetRequiredService<IMultiTenantContextAccessor<TenantDetails>>().MultiTenantContext?.TenantInfo);
        Assert.Null(bus.TenantId);

        await bus.InvokeForTenantAsync(
            tenantId,
            new RetryPaymentCommand(orderId, 50m, "USD", "pm_unseeded_envelope", NewRequestId(), $"retry-{orderId:N}"),
            CancellationToken.None);

        Assert.Equal(1, Provider.AttemptCalls);
        Assert.Equal(orderId, Assert.Single(Provider.AttemptRequests).OrderId);
    }

    [Fact]
    public async Task PersistedAttemptBeforeOutcome_RedeliveredOutcomeConvergesWithoutDuplicateProviderCalls()
    {
        var orderId = Guid.NewGuid();
        var requestId = NewRequestId();
        Provider.QueueAttemptResults(RecordingPaymentProvider.Outcome("processing"));

        await DeliverOrderAsync(CreateOrder(orderId, requestId));
        await WaitForAsync(() => GetPaymentAsync(orderId), payment => payment?.Status == "Pending");

        var accepted = await InvokeForTenantAndTrackAsync(new ProcessPaymentOutcomeCommand(orderId, requestId, "succeeded", "delayed-reference", null));
        var duplicate = await InvokeForTenantAndTrackAsync(new ProcessPaymentOutcomeCommand(orderId, requestId, "succeeded", "delayed-reference", null));
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

        var initial = await InvokeForTenantAndTrackAsync(CreateOrder(orderId, originalRequestId));
        var replay = await InvokeForTenantAndTrackAsync(CreateOrder(orderId, NewRequestId()));
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

        var first = await InvokeForTenantAndTrackAsync(CreateOrder(orderId, requestId));
        var replay = await InvokeForTenantAndTrackAsync(CreateOrder(orderId, requestId));

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

        var tracked = await InvokeForTenantAndTrackAsync(new CapturePaymentCommand(orderId, customerId, 42.50m, "USD"));
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
        var firstRetry = await InvokeForTenantAndTrackAsync(retry);
        var duplicateRetry = await InvokeForTenantAndTrackAsync(retry);

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

        var tracked = await InvokeForTenantAndTrackAsync(CreateOrder(orderId, requestId));
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
    [InlineData("issuer-contact-required")]
    public async Task NonTransientOutcome_DoesNotScheduleAutomaticProviderRetry(string outcome)
    {
        var orderId = Guid.NewGuid();
        if (outcome == "issuer-contact-required")
        {
            const string providerCode = "issuer_contact_required";
            SetPaymentProviderSetting($"DeclineMappings:{providerCode}", outcome);
            var options = Services.GetRequiredService<IOptionsMonitor<PaymentProviderOptions>>();
            await WaitForAsync(
                () => Task.FromResult(options.CurrentValue.DeclineMappings.TryGetValue(providerCode, out var value) ? value : null),
                value => value == outcome);
            Provider.QueueAttemptResults(RecordingPaymentProvider.Outcome("failed", providerCode));
        }
        else
        {
            Provider.QueueAttemptResults(RecordingPaymentProvider.Outcome(outcome, "generic_decline"));
        }

        await DeliverOrderAsync(CreateOrder(orderId, NewRequestId()));
        var payment = await WaitForAsync(() => GetPaymentAsync(orderId), candidate => candidate?.Status == "Failed");
        var persisted = await GetPersistedPaymentAsync(orderId);

        Assert.NotNull(payment);
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        Assert.Equal(1, Provider.AttemptCalls);
        Assert.Single(persisted.Attempts);

        if (outcome == "issuer-contact-required")
        {
            Assert.Equal(DeclineCategory.IssuerContactRequired, persisted.DeclineCategory);
        }
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

    // The assertion belongs with the deferred tenant-scoped payment index migration (issue #577).
    [Fact(Skip = "Blocked by issue #577: the deferred tenant-scoped IX_payments(OrderId) migration.")]
    public async Task TenantCollidingOrderIds_ArePersistedAsIndependentPaymentsAndProviderCalls()
    {
        var orderId = Guid.NewGuid();
        Provider.QueueAttemptResults(RecordingPaymentProvider.Outcome("succeeded"), RecordingPaymentProvider.Outcome("succeeded"));

        await DeliverForTenantAsync("tenant-a", CreateOrder(orderId, NewRequestId(), tenantId: "tenant-a"));
        await DeliverForTenantAsync("tenant-b", CreateOrder(orderId, NewRequestId(), tenantId: "tenant-b"));

        await WaitForAsync(() => GetPersistedPaymentsAsync(orderId), payments => payments.Count == 2);
        var payments = await GetPersistedPaymentsAsync(orderId);

        Assert.Equal(2, Provider.AttemptCalls);
        Assert.Equal(["tenant-a", "tenant-b"], payments.Select(payment => payment.TenantId).Order());
        Assert.All(payments, payment => Assert.Single(payment.Attempts));
    }

    [Fact]
    public async Task ForeignTenantInvoice_IsNotFound()
    {
        var sameTenantOrderId = Guid.NewGuid();
        var foreignTenantOrderId = Guid.NewGuid();
        Provider.QueueAttemptResults(
            RecordingPaymentProvider.Outcome("succeeded"),
            RecordingPaymentProvider.Outcome("succeeded"));

        await DeliverOrderAsync(CreateOrder(sameTenantOrderId, NewRequestId()));
        await DeliverForTenantAsync("tenant-b", CreateOrder(foreignTenantOrderId, NewRequestId(), tenantId: "tenant-b"));
        var sameTenantInvoice = Assert.Single(await GetInvoicesAsync(sameTenantOrderId));
        var foreignTenantInvoice = Assert.Single(await GetInvoicesAsync(foreignTenantOrderId, "tenant-b"));

        var sameTenantResponse = await Client.GetAsync($"/invoices/{sameTenantInvoice.Id}");
        var foreignTenantResponse = await Client.GetAsync($"/invoices/{foreignTenantInvoice.Id}");

        Assert.Equal(System.Net.HttpStatusCode.OK, sameTenantResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, foreignTenantResponse.StatusCode);

        await using (var scope = Services.CreateAsyncScope())
        {
            EstablishMessageTenant(scope.ServiceProvider, MockBearerAuthenticationHandler.TestTenantId);
            var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();

            Assert.Equal(MockBearerAuthenticationHandler.TestTenantId, context.TenantId);
            Assert.Null(await context.Invoices.SingleOrDefaultAsync(invoice => invoice.Id == foreignTenantInvoice.Id));

            var readContext = scope.ServiceProvider.GetRequiredService<BillingReadDbContext>();
            Assert.Equal(MockBearerAuthenticationHandler.TestTenantId, readContext.TenantId);
            Assert.Null(await readContext.Invoices.SingleOrDefaultAsync(invoice => invoice.Id == foreignTenantInvoice.Id));
            var evaluator = new Ardalis.Specification.EntityFrameworkCore.SpecificationEvaluator();
            Assert.Null(await evaluator.GetQuery(readContext.Invoices.AsQueryable(), new InvoiceByIdSpec(foreignTenantInvoice.Id)).SingleOrDefaultAsync());
        }

        await using (var scope = Services.CreateAsyncScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            bus.TenantId = MockBearerAuthenticationHandler.TestTenantId;

            Assert.Null(await bus.InvokeAsync<InvoiceDto>(new GetInvoiceQuery(foreignTenantInvoice.Id)));
        }
    }

    [Fact]
    public async Task PaymentCreatedForForeignTenant_IsNotFoundAndExcludedFromList()
    {
        var orderId = Guid.NewGuid();
        Provider.QueueAttemptResults(RecordingPaymentProvider.Outcome("succeeded"));

        await DeliverForTenantAsync("tenant-b", CreateOrder(orderId, NewRequestId()));
        var foreignPayment = (await GetPersistedPaymentsAsync(orderId, Guid.NewGuid())).Single();

        var getResponse = await Client.GetAsync(new Uri($"/payments/{foreignPayment.Id}", UriKind.Relative));
        var listResponse = await Client.GetAsync(new Uri("/payments", UriKind.Relative));
        var payments = await listResponse.Content.ReadFromJsonAsync<IReadOnlyList<PaymentDto>>();

        Assert.Equal(System.Net.HttpStatusCode.NotFound, getResponse.StatusCode);
        listResponse.EnsureSuccessStatusCode();
        Assert.DoesNotContain(payments ?? [], payment => payment.Id == foreignPayment.Id);
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
        await scope.ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeForTenantAsync(tenantId, evt, CancellationToken.None)
            .ConfigureAwait(false);
    }

    private async Task DeliverOrderAsync(OrderPlacedV2IntegrationEvent evt) => await DeliverForTenantAsync(evt.TenantId, evt);

    private static OrderPlacedV2IntegrationEvent CreateOrder(
        Guid orderId,
        string requestId,
        decimal amount = 42.50m,
        decimal authorizedAmount = 50m,
        string currency = "USD",
        string token = "pm_lifecycle_token",
        string tenantId = MockBearerAuthenticationHandler.TestTenantId) =>
        new()
        {
            OrderId = orderId,
            BasketId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            KeycloakSubjectId = "test-user",
            TenantId = tenantId,
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
        EstablishMessageTenant(scope.ServiceProvider, MockBearerAuthenticationHandler.TestTenantId);
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        return await context.Payments
            .Where(payment => payment.OrderId == orderId)
            .Select(payment => new PaymentDto(payment.Id, payment.OrderId, payment.CustomerId, payment.Amount.Amount, payment.Amount.Currency, payment.Status.Name, payment.ProviderReference))
            .SingleOrDefaultAsync();
    }

    private async Task<Payment> GetPersistedPaymentAsync(Guid orderId)
    {
        await using var scope = Services.CreateAsyncScope();
        EstablishMessageTenant(scope.ServiceProvider, MockBearerAuthenticationHandler.TestTenantId);
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        return await context.Payments.Include(payment => payment.Attempts).SingleAsync(payment => payment.OrderId == orderId);
    }

    private async Task<List<Payment>> GetPersistedPaymentsAsync(params Guid[] orderIds)
    {
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        return await context.Payments.IgnoreQueryFilters([Constants.TenantToken]).Include(payment => payment.Attempts)
            .Where(payment => orderIds.Contains(payment.OrderId))
            .ToListAsync();
    }

    private async Task<List<Payment>> GetAllPaymentsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Payments.IgnoreQueryFilters([Constants.TenantToken]).ToListAsync();
    }

    private async Task<List<Invoice>> GetInvoicesAsync(Guid orderId, string tenantId = MockBearerAuthenticationHandler.TestTenantId)
    {
        await using var scope = Services.CreateAsyncScope();
        EstablishMessageTenant(scope.ServiceProvider, tenantId);
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

    private async Task SeedPendingPaymentWithoutMessageTenantAsync(string tenantId, Guid orderId)
    {
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO payments (
                "Id", "TenantId", "OrderId", "CustomerId", "Amount", "Currency", "Status", "ProviderReference", "CreatedAt", "IsDeleted",
                "AuthorizedAmount", "AuthorizedCurrency", "PaymentMethodToken", "RequestId", "SourceCorrelationId")
            VALUES (
                {Guid.NewGuid()}, {tenantId}, {orderId}, {Guid.NewGuid()}, {42.50m}, {"USD"}, {PaymentStatus.Pending.Value}, {null}, {DateTimeOffset.UtcNow}, {false},
                {50m}, {"USD"}, {"pm_seeded_pending"}, {$"seed-{orderId:N}"}, {string.Empty})
            """);
    }

    private async Task<ITrackedSession> InvokeForTenantAndTrackAsync(object message)
    {
        Func<IMessageContext, Task> invoke = async _ =>
        {
            using var scope = Services.CreateScope();
            EstablishMessageTenant(scope.ServiceProvider, MockBearerAuthenticationHandler.TestTenantId);
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await bus
                .InvokeAsync(message, CancellationToken.None)
                .ConfigureAwait(false);
        };

        return await Services.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(10))
            .ExecuteAndWaitAsync(invoke)
            .ConfigureAwait(false);
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

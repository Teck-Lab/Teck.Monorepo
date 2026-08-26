using System.Text;
using Orders.Domain.DomainEvents;
using Orders.Domain.Services;
using Orders.Domain.ValueObjects;
using SharedKernel.Core.Domain;

namespace Orders.Domain.Entities;

/// <summary>Represents the tenant-scoped order lifecycle aggregate.</summary>
public sealed class Order : BaseEntity, IAggregateRoot, ITenantScoped
{
    private const string PendingBackorderReadyPrefix = "pending-backorder-ready:";

    private Order() => Lines = [];

    /// <summary>Gets the optional customer correlation known at checkout.</summary>
    public Guid CustomerId { get; private set; }

    /// <inheritdoc/>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets the immutable Keycloak subject that owns payment retry.</summary>
    public string KeycloakSubjectId { get; private set; } = string.Empty;

    /// <summary>Gets the originating basket identifier.</summary>
    public Guid BasketId { get; private set; }

    /// <summary>Gets the current readable lifecycle status.</summary>
    public OrderStatus Status { get; private set; } = OrderStatus.Pending;

    /// <summary>Gets the payment substate.</summary>
    public PaymentState PaymentState { get; private set; } = PaymentState.Pending;

    /// <summary>Gets the stock substate.</summary>
    public StockState StockState { get; private set; } = StockState.Pending;

    /// <summary>Gets the safe reason for a failed lifecycle transition.</summary>
    public OrderFailureReason FailureReason { get; private set; } = OrderFailureReason.None;

    /// <summary>Gets the shopper-safe action required after a payment failure.</summary>
    public string ActionText { get; private set; } = string.Empty;

    /// <summary>Gets whether a captured but unfulfillable order needs human action.</summary>
    public bool RequiresHumanDecision { get; private set; }

    /// <summary>Gets the lines that make up the order.</summary>
    public List<OrderLine> Lines { get; private set; }

    /// <summary>Gets the platform-resolved total.</summary>
    public decimal Total { get; private set; }

    /// <summary>Gets the shopper-authorized payment ceiling.</summary>
    public decimal AuthorizedAmount { get; private set; }

    /// <summary>Gets the amount captured by the provider.</summary>
    public decimal CapturedAmount { get; private set; }

    /// <summary>Gets the ISO currency agreed at checkout.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>Gets the latest payment identifier when known.</summary>
    public Guid? PaymentId { get; private set; }

    /// <summary>Gets the stable checkout correlation identifier.</summary>
    public string CheckoutCorrelationId { get; private set; } = string.Empty;

    /// <summary>Gets the latest accepted retry request identifier.</summary>
    public string? RetryRequestId { get; private set; }

    /// <summary>Gets whether the order may request another payment attempt.</summary>
    public bool CanRetryPayment => Status == OrderStatus.Pending && PaymentState == PaymentState.ActionRequired;

    /// <summary>Gets the persisted keys of lifecycle messages already applied.</summary>
    public string ProcessedTransitionKeys { get; private set; } = string.Empty;

    /// <summary>Creates an order from an authoritative version-two checkout.</summary>
    /// <param name="customerId">The optional customer correlation.</param>
    /// <param name="keycloakSubjectId">The immutable Keycloak subject.</param>
    /// <param name="basketId">The originating basket identifier.</param>
    /// <param name="tenantId">The owning tenant identifier.</param>
    /// <param name="lines">The platform-priced lines.</param>
    /// <param name="authorizedAmount">The shopper-authorized ceiling.</param>
    /// <param name="currency">The ISO checkout currency.</param>
    /// <param name="checkoutCorrelationId">The stable checkout idempotency key.</param>
    /// <returns>The newly created pending order.</returns>
    public static Order Create(
        Guid? customerId,
        string keycloakSubjectId,
        Guid basketId,
        string tenantId,
        List<OrderLine> lines,
        decimal authorizedAmount,
        string currency,
        string checkoutCorrelationId)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0 || string.IsNullOrWhiteSpace(keycloakSubjectId) || authorizedAmount <= 0 || string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Checkout must provide lines, an owner, a positive ceiling, and currency.");
        }

        var total = OrderPricingService.CalculateTotal(lines);
        if (total > authorizedAmount)
        {
            throw new InvalidOperationException("The platform-resolved total exceeds the authorized amount.");
        }

        return new Order
        {
            CustomerId = customerId ?? Guid.Empty,
            KeycloakSubjectId = keycloakSubjectId,
            BasketId = basketId,
            TenantId = tenantId,
            Lines = new List<OrderLine>(lines),
            Total = total,
            AuthorizedAmount = authorizedAmount,
            Currency = currency,
            CheckoutCorrelationId = checkoutCorrelationId,
        };
    }

    /// <summary>Creates a legacy in-process order used only by pre-lifecycle callers and tests.</summary>
    /// <param name="customerId">The customer identifier.</param>
    /// <param name="tenantId">The owning tenant identifier.</param>
    /// <param name="lines">The order lines.</param>
    /// <returns>A pending order with a ceiling equal to its resolved total.</returns>
    public static Order Create(Guid customerId, string tenantId, List<OrderLine> lines)
    {
        var total = OrderPricingService.CalculateTotal(lines);
        return Create(customerId, "legacy-subject", Guid.Empty, tenantId, lines, total, "USD", Guid.NewGuid().ToString("N"));
    }

    /// <summary>Records a safe payment capture and derives any newly-confirmed lifecycle event.</summary>
    /// <param name="paymentId">The provider-agnostic payment identifier.</param>
    /// <param name="amount">The captured amount.</param>
    /// <param name="transitionKey">The idempotency key for the outcome.</param>
    /// <param name="sourceCorrelationId">The originating lifecycle correlation.</param>
    /// <returns>A notification event when this capture confirms the order; otherwise null.</returns>
    public object? ApplyPaymentCaptured(Guid paymentId, decimal amount, string transitionKey, string sourceCorrelationId)
    {
        if (amount <= 0 || amount > AuthorizedAmount)
        {
            return null;
        }

        if (!TryRecordTransition(transitionKey))
        {
            return null;
        }

        PaymentId = paymentId;
        CapturedAmount = amount;
        PaymentState = PaymentState.Captured;
        if (StockState != StockState.Rejected && StockState != StockState.Expired)
        {
            ClearFailureAction();
        }

        return Reconcile(sourceCorrelationId);
    }

    /// <summary>Records a safe payment failure and derives one shopper-action event.</summary>
    /// <param name="category">The safe billing decline category.</param>
    /// <param name="actionText">The shopper-safe action text.</param>
    /// <param name="transitionKey">The idempotency key for the outcome.</param>
    /// <param name="sourceCorrelationId">The originating lifecycle correlation.</param>
    /// <returns>A payment-action notification, or null for a duplicate.</returns>
    public object? ApplyPaymentFailure(string category, string actionText, string transitionKey, string sourceCorrelationId)
    {
        if (!TryRecordTransition(transitionKey))
        {
            return null;
        }

        if (PaymentState == PaymentState.Captured)
        {
            return null;
        }

        if (StockState == StockState.Rejected || StockState == StockState.Expired)
        {
            return null;
        }

        PaymentState = PaymentState.ActionRequired;
        FailureReason = OrderFailureReason.FromCategory(category);
        ActionText = actionText;
        return new OrderPaymentActionRequired(Id, CustomerId, KeycloakSubjectId, TenantId, FailureReason.Name, actionText, $"payment-action:{transitionKey}", sourceCorrelationId);
    }

    /// <summary>Records an idempotent successful stock reservation.</summary>
    /// <param name="transitionKey">The idempotency key for the stock outcome.</param>
    /// <param name="sourceCorrelationId">The originating lifecycle correlation.</param>
    /// <returns>A confirmation notification when payment is already captured; otherwise null.</returns>
    public object? ApplyStockReserved(string transitionKey, string sourceCorrelationId) =>
        ApplyStockReserved(hasOutstandingBackorder: false, transitionKey, sourceCorrelationId);

    /// <summary>Records an idempotent V2 stock reservation, retaining a backorder wait when stock remains outstanding.</summary>
    /// <param name="hasOutstandingBackorder">Whether any reserved line still has a backordered quantity.</param>
    /// <param name="transitionKey">The idempotency key for the stock outcome.</param>
    /// <param name="sourceCorrelationId">The originating lifecycle correlation.</param>
    /// <returns>A confirmation notification when fully reserved stock meets an already captured payment; otherwise null.</returns>
    public object? ApplyStockReserved(bool hasOutstandingBackorder, string transitionKey, string sourceCorrelationId)
    {
        if (!TryRecordTransition(transitionKey))
        {
            return null;
        }

        if (StockState != StockState.Pending)
        {
            return null;
        }

        if (hasOutstandingBackorder)
        {
            StockState = StockState.Backordered;
            return null;
        }

        StockState = StockState.Reserved;
        return Reconcile(sourceCorrelationId);
    }

    /// <summary>Records a stock rejection, preserving the captured-money escalation precedence.</summary>
    /// <param name="transitionKey">The idempotency key for the stock outcome.</param>
    /// <param name="sourceCorrelationId">The originating lifecycle correlation.</param>
    /// <param name="actionText">The shopper-safe action text.</param>
    /// <returns>A one-time rejection notification, or null for a duplicate.</returns>
    public object? ApplyStockRejected(string transitionKey, string sourceCorrelationId, string actionText)
    {
        if (!TryRecordTransition(transitionKey))
        {
            return null;
        }

        StockState = StockState.Rejected;
        NormalizeNonCapturedPaymentState();
        FailureReason = OrderFailureReason.StockRejected;
        ActionText = actionText;
        var escalation = Reconcile(sourceCorrelationId, actionText);
        if (escalation is not null)
        {
            return escalation;
        }

        Status = OrderStatus.Cancelled;
        return new OrderRejected(Id, CustomerId, KeycloakSubjectId, TenantId, FailureReason.Name, actionText, $"order-rejected:{transitionKey}", sourceCorrelationId);
    }

    /// <summary>Moves a backorder into price-check pending state.</summary>
    /// <param name="transitionKey">The idempotency key for the ready outcome.</param>
    /// <param name="sourceCorrelationId">The source correlation for the ready outcome.</param>
    /// <returns>True when a price check must be requested.</returns>
    public bool ApplyBackorderReady(string transitionKey, string sourceCorrelationId)
    {
        if (StockState == StockState.Pending)
        {
            return !HasPendingBackorderReady() && TryRecordTransition(GetPendingBackorderReadyMarker(transitionKey, sourceCorrelationId));
        }

        if (StockState != StockState.Backordered)
        {
            return false;
        }

        if (!TryRecordTransition(transitionKey))
        {
            return false;
        }

        StockState = StockState.AwaitingPriceCheck;
        return true;
    }

    /// <summary>Consumes an early-ready fact after a delayed backordered reservation is observed.</summary>
    /// <param name="idempotencyKey">Receives the original ready idempotency key when available.</param>
    /// <param name="sourceCorrelationId">Receives the original ready source correlation when available.</param>
    /// <returns>True when one persisted early-ready fact advanced the order to price-check pending.</returns>
    public bool TryConsumePendingBackorderReady(out string idempotencyKey, out string sourceCorrelationId)
    {
        idempotencyKey = string.Empty;
        sourceCorrelationId = string.Empty;
        if (StockState != StockState.Backordered)
        {
            return false;
        }

        var keys = ProcessedTransitionKeys.Split('|', StringSplitOptions.RemoveEmptyEntries);
        var marker = keys.FirstOrDefault(key => key.StartsWith(PendingBackorderReadyPrefix, StringComparison.Ordinal));
        if (marker is null || !TryReadPendingBackorderReadyMarker(marker, out idempotencyKey, out sourceCorrelationId))
        {
            return false;
        }

        ProcessedTransitionKeys = string.Join('|', keys.Where(key => !string.Equals(key, marker, StringComparison.Ordinal)));
        StockState = StockState.AwaitingPriceCheck;
        return true;
    }

    /// <summary>Records expiry of a backorder and emits a single safe cancellation.</summary>
    /// <param name="transitionKey">The idempotency key for the expiry outcome.</param>
    /// <param name="sourceCorrelationId">The originating lifecycle correlation.</param>
    /// <returns>A cancellation or human-decision rejection notification, or null for a duplicate.</returns>
    public object? ApplyBackorderExpired(string transitionKey, string sourceCorrelationId)
    {
        if (!TryRecordTransition(transitionKey))
        {
            return null;
        }

        StockState = StockState.Expired;
        NormalizeNonCapturedPaymentState();
        FailureReason = OrderFailureReason.BackorderExpired;
        const string actionText = "Your backordered items were not available in time.";
        ActionText = actionText;
        var escalation = Reconcile(sourceCorrelationId, actionText);
        if (escalation is not null)
        {
            return escalation;
        }

        Status = OrderStatus.Cancelled;
        return new OrderCancelled(Id, CustomerId, KeycloakSubjectId, TenantId, actionText, $"order-cancelled:{transitionKey}", sourceCorrelationId);
    }

    /// <summary>Applies an authoritative backorder price result.</summary>
    /// <param name="withinCeiling">Whether the resolved amount is within authorization.</param>
    /// <param name="transitionKey">The idempotency key for the price result.</param>
    /// <param name="sourceCorrelationId">The originating lifecycle correlation.</param>
    /// <returns>A confirmation or rejection notification, or null for a duplicate.</returns>
    public object? ApplyBackorderPriceChecked(bool withinCeiling, string transitionKey, string sourceCorrelationId)
    {
        if (!TryRecordTransition(transitionKey))
        {
            return null;
        }

        if (withinCeiling)
        {
            if (StockState != StockState.AwaitingPriceCheck)
            {
                return null;
            }

            StockState = StockState.Reserved;
            return Reconcile(sourceCorrelationId);
        }

        StockState = StockState.Rejected;
        NormalizeNonCapturedPaymentState();
        FailureReason = OrderFailureReason.PriceExceededAuthorization;
        const string actionText = "The current price exceeds your authorized amount.";
        ActionText = actionText;
        var escalation = Reconcile(sourceCorrelationId, actionText);
        if (escalation is not null)
        {
            return escalation;
        }

        Status = OrderStatus.Rejected;
        return new OrderRejected(Id, CustomerId, KeycloakSubjectId, TenantId, FailureReason.Name, actionText, $"order-rejected:{transitionKey}", sourceCorrelationId);
    }

    /// <summary>Gets whether a retry request has already been durably recorded.</summary>
    /// <param name="requestId">The stable retry request identifier.</param>
    /// <returns>True when the request was already accepted for this order.</returns>
    public bool HasRecordedRetryRequest(string requestId) =>
        !string.IsNullOrWhiteSpace(requestId) &&
        ProcessedTransitionKeys.Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Contains(GetRetryTransitionKey(requestId), StringComparer.Ordinal);

    /// <summary>Records the request key used for an owner-authorized payment retry.</summary>
    /// <param name="requestId">The stable retry request identifier.</param>
    /// <returns>True when the eligible retry was accepted for the first time.</returns>
    public bool BeginRetry(string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId) || !CanRetryPayment || !TryRecordTransition(GetRetryTransitionKey(requestId)))
        {
            return false;
        }

        RetryRequestId = requestId;
        PaymentState = PaymentState.Pending;
        ClearFailureAction();
        return true;
    }

    private static string GetRetryTransitionKey(string requestId) => $"payment-retry:{EncodeRetryRequestId(requestId)}";

    private static string EncodeRetryRequestId(string requestId) => Convert.ToBase64String(Encoding.UTF8.GetBytes(requestId))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static string GetPendingBackorderReadyMarker(string idempotencyKey, string sourceCorrelationId) =>
        $"{PendingBackorderReadyPrefix}{EncodeRetryRequestId(idempotencyKey)}:{EncodeRetryRequestId(sourceCorrelationId)}";

    private static bool TryReadPendingBackorderReadyMarker(string marker, out string idempotencyKey, out string sourceCorrelationId)
    {
        idempotencyKey = string.Empty;
        sourceCorrelationId = string.Empty;
        var encoded = marker[PendingBackorderReadyPrefix.Length..].Split(':', 2);
        if (encoded.Length != 2)
        {
            return false;
        }

        try
        {
            idempotencyKey = DecodeRetryRequestId(encoded[0]);
            sourceCorrelationId = DecodeRetryRequestId(encoded[1]);
            return !string.IsNullOrWhiteSpace(idempotencyKey) && !string.IsNullOrWhiteSpace(sourceCorrelationId);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string DecodeRetryRequestId(string encoded) => Encoding.UTF8.GetString(Convert.FromBase64String(encoded
        .Replace('-', '+')
        .Replace('_', '/')
        .PadRight((encoded.Length + 3) / 4 * 4, '=')));

    private bool HasPendingBackorderReady() => ProcessedTransitionKeys.Split('|', StringSplitOptions.RemoveEmptyEntries)
        .Any(key => key.StartsWith(PendingBackorderReadyPrefix, StringComparison.Ordinal));

    private object? Reconcile(string sourceCorrelationId, string? unfulfillableActionText = null)
    {
        if (PaymentState != PaymentState.Captured)
        {
            return null;
        }

        if (StockState == StockState.Reserved)
        {
            if (Status == OrderStatus.Confirmed)
            {
                return null;
            }

            Status = OrderStatus.Confirmed;
            ClearFailureAction();
            return new OrderConfirmed(Id, CustomerId, KeycloakSubjectId, TenantId, Total, Currency, $"order-confirmed:{Id:N}", sourceCorrelationId, AuthorizedAmount);
        }

        if ((StockState != StockState.Rejected && StockState != StockState.Expired) || Status == OrderStatus.PaidUnfulfillable)
        {
            return null;
        }

        Status = OrderStatus.PaidUnfulfillable;
        RequiresHumanDecision = true;
        ActionText = unfulfillableActionText ?? GetUnfulfillableActionText();
        return new OrderRejected(
            Id,
            CustomerId,
            KeycloakSubjectId,
            TenantId,
            FailureReason.Name,
            ActionText,
            $"order-rejected:{Id:N}",
            sourceCorrelationId);
    }

    private string GetUnfulfillableActionText() => FailureReason switch
    {
        var reason when reason == OrderFailureReason.PriceExceededAuthorization => "The current price exceeds your authorized amount.",
        _ => "Your order cannot currently be fulfilled.",
    };

    private void ClearFailureAction()
    {
        FailureReason = OrderFailureReason.None;
        ActionText = string.Empty;
    }

    private void NormalizeNonCapturedPaymentState()
    {
        if (PaymentState != PaymentState.Captured)
        {
            PaymentState = PaymentState.Pending;
        }
    }

    private bool TryRecordTransition(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("A lifecycle message must include an idempotency key.", nameof(key));
        }

        var keys = ProcessedTransitionKeys.Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (keys.Contains(key, StringComparer.Ordinal))
        {
            return false;
        }

        ProcessedTransitionKeys = string.IsNullOrWhiteSpace(ProcessedTransitionKeys) ? key : $"{ProcessedTransitionKeys}|{key}";
        return true;
    }
}

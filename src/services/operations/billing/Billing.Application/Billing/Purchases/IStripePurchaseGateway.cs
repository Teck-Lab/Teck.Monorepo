using Billings.Domain.Entities;

namespace Billings.Application.Billing.Purchases;

/// <summary>Creates an unconfirmed provider intent and verifies a captured intent against the immutable order.</summary>
public interface IStripePurchaseGateway
{
    /// <summary>Creates one intent from the server-owned order amount and identifiers.</summary>
    /// <param name="order">The pending purchase.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>The provider-created intent ID and client secret.</returns>
    Task<StripePurchaseIntent> CreateAsync(TenantPurchaseOrder order, CancellationToken cancellationToken);

    /// <summary>Retrieves the existing intent so a retry can return its client secret.</summary>
    /// <param name="intentId">The bound Stripe intent ID.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>The provider intent.</returns>
    Task<StripePurchaseIntent> GetAsync(string intentId, CancellationToken cancellationToken);

    /// <summary>Retrieves Stripe's captured state and matches exact order, tenant, amount and currency.</summary>
    /// <param name="order">The immutable purchase snapshot carrying its bound provider intent ID.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>True only for a matching, fully succeeded capture.</returns>
    Task<bool> IsCapturedAsync(TenantPurchaseOrder order, CancellationToken cancellationToken);
}

/// <summary>The non-card-data intent values a future Stripe.js client requires.</summary>
/// <param name="PaymentIntentId">Provider-created PaymentIntent ID.</param>
/// <param name="ClientSecret">Provider-created confirmation client secret.</param>
public sealed record StripePurchaseIntent(string PaymentIntentId, string ClientSecret);

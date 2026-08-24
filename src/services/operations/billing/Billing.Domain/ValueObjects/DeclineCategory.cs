using Ardalis.SmartEnum;

namespace Billings.Domain.ValueObjects;

/// <summary>Represents the shopper-safe category of a provider decline.</summary>
public sealed class DeclineCategory : SmartEnum<DeclineCategory>
{
    /// <summary>A temporary problem that can be retried automatically.</summary>
    public static readonly DeclineCategory Transient = new("transient", 1);

    /// <summary>The shopper must complete authentication.</summary>
    public static readonly DeclineCategory AuthenticationRequired = new("authentication-required", 2);

    /// <summary>The shopper must provide a replacement payment method.</summary>
    public static readonly DeclineCategory PaymentMethodRequired = new("payment-method-required", 3);

    /// <summary>The shopper must contact their issuer.</summary>
    public static readonly DeclineCategory IssuerContactRequired = new("issuer-contact-required", 4);

    /// <summary>A generic decline which never reveals sensitive provider detail.</summary>
    public static readonly DeclineCategory GenericDecline = new("generic-decline", 5);

    private DeclineCategory(string name, int value)
        : base(name, value)
    {
    }
}

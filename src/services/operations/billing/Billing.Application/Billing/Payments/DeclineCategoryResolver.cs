using System.Security.Cryptography;
using System.Text;
using Billings.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace Billings.Application.Billing.Payments;

/// <summary>Maps provider-private decline codes to shopper-safe categories using reloadable options.</summary>
/// <param name="options">The reloadable payment-provider options.</param>
public sealed class DeclineCategoryResolver(IOptionsMonitor<PaymentProviderOptions> options)
{
    private static readonly HashSet<string> SensitiveCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "fraudulent",
        "lost_card",
        "stolen_card",
        "block_list",
    };

    /// <summary>Resolves a provider code and returns a safe mapping audit hash.</summary>
    /// <param name="providerCode">The private code returned by the provider.</param>
    /// <returns>The safe category and a SHA-256 audit hash.</returns>
    public DeclineResolution Resolve(string? providerCode)
    {
        var code = providerCode?.Trim() ?? string.Empty;
        var configured = options.CurrentValue.DeclineMappings.TryGetValue(code, out var value) ? value : null;
        var category = SensitiveCodes.Contains(code)
            ? DeclineCategory.GenericDecline
            : ToCategory(configured);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{code}:{category.Name}"))).ToLowerInvariant();
        return new DeclineResolution(category, hash);
    }

    private static DeclineCategory ToCategory(string? configured) => configured?.Trim().ToLowerInvariant() switch
    {
        "transient" => DeclineCategory.Transient,
        "authentication-required" => DeclineCategory.AuthenticationRequired,
        "payment-method-required" => DeclineCategory.PaymentMethodRequired,
        "issuer-contact-required" => DeclineCategory.IssuerContactRequired,
        _ => DeclineCategory.GenericDecline,
    };
}

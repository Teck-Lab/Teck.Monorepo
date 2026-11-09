namespace Billings.Host.Infrastructure;

/// <summary>Billing's Keycloak service-client and Customer reservation API settings.</summary>
public sealed class CustomerReservationOptions
{
    /// <summary>Configuration section key.</summary>
    public const string SectionName = "CustomerReservations";

    /// <summary>Gets or sets the OAuth token endpoint.</summary>
    public string TokenEndpoint { get; set; } = string.Empty;

    /// <summary>Gets or sets the OAuth confidential client identifier.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Gets or sets the runtime-injected OAuth confidential client secret.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Gets or sets the authorized Customer reservation scope.</summary>
    public string Scope { get; set; } = "tenant-purchase customer-api-audience";

    /// <summary>Gets or sets the Customer service base URL.</summary>
    public string CustomerBaseUrl { get; set; } = string.Empty;
}

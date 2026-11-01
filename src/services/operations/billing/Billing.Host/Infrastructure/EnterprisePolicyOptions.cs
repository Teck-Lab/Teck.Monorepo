namespace Billings.Host.Infrastructure;

/// <summary>Licensing internal gRPC endpoint configuration.</summary>
public sealed class EnterprisePolicyOptions
{
    /// <summary>Configuration section.</summary>
    public const string SectionName = "EnterprisePolicy";

    /// <summary>Cluster-discovery address for Licensing; network policy is the trust boundary.</summary>
    public string Address { get; set; } = "http://licensing:8080";
}

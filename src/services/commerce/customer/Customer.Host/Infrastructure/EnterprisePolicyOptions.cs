namespace Customers.Host.Infrastructure;

/// <summary>Licensing internal gRPC service discovery options.</summary>
public sealed class EnterprisePolicyOptions
{
    /// <summary>Configuration section.</summary>
    public const string SectionName = "EnterprisePolicy";
    /// <summary>Licensing service discovery address.</summary>
    public string Address { get; set; } = "http://licensing:8080";
}

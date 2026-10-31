namespace Billings.Host.Endpoints.Purchases;

/// <summary>Customer-submitted tenant purchase options.</summary>
public sealed class CreateTenantPurchaseRequest
{
    /// <summary>Gets or sets the Customer-issued reservation identifier.</summary>
    public Guid ReservationId { get; set; }

    /// <summary>Gets or sets the beneficiary subject for an audited sales-assisted order.</summary>
    public string? BeneficiarySubjectId { get; set; }

    /// <summary>Gets or sets the plan name.</summary>
    public string PlanId { get; set; } = string.Empty;

    /// <summary>Gets or sets the plan version.</summary>
    public string PlanVersion { get; set; } = string.Empty;

    /// <summary>Gets or sets the selected database provider.</summary>
    public string DatabaseProvider { get; set; } = "postgres";

    /// <summary>Gets or sets whether to include read replicas.</summary>
    public bool HasReadReplicas { get; set; }

    /// <summary>Gets or sets the requested replica count.</summary>
    public int ReplicaCount { get; set; }
}

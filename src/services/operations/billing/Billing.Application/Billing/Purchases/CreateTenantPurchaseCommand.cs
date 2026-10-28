namespace Billings.Application.Billing.Purchases;

/// <summary>Purchase intent from the authenticated tenant or sales-assisted workflow.</summary>
/// <param name="TenantId">Global Customer tenant identifier.</param>
/// <param name="TenantIdentifier">Tenant slug.</param>
/// <param name="PlanId">Plan name.</param>
/// <param name="PlanVersion">Current immutable server plan version.</param>
/// <param name="DatabaseProvider">Database provider.</param>
/// <param name="HasReadReplicas">Whether read replicas were requested.</param>
/// <param name="ReplicaCount">Requested replica count.</param>
/// <param name="ActorSubjectId">Authenticated purchaser or authorized sales actor.</param>
/// <param name="BeneficiarySubjectId">Customer-verified owner of the reservation.</param>
/// <param name="IsRenewal">Whether this is a new annual renewal rather than the original order retry.</param>
/// <param name="PreviousPurchaseOrderId">Customer's correlated paid source order for a renewal.</param>
public sealed record CreateTenantPurchaseCommand(Guid TenantId, string TenantIdentifier, string PlanId, string PlanVersion, string DatabaseProvider, bool HasReadReplicas, int ReplicaCount, string ActorSubjectId, string BeneficiarySubjectId, bool IsRenewal = false, Guid PreviousPurchaseOrderId = default);

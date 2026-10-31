using SharedKernel.Events;

namespace Customers.Application.Tenants;

/// <summary>Distinguishes absent tenant, first attestation and fully signed second approval.</summary>
/// <param name="Found">Whether the canonical tenant exists.</param>
/// <param name="Decision">The signed purge approval after two independent operators, if complete.</param>
public sealed record TenantPurgeApprovalResult(bool Found, TenantPurgeApprovedIntegrationEvent? Decision);

namespace Customers.Host.Endpoints.Tenants;

/// <summary>Pending tenant reservation owned by the authenticated subject.</summary>
public sealed record TenantOnboardingDto(Guid TenantId, string Identifier, string Status, string OwnerSubjectId);

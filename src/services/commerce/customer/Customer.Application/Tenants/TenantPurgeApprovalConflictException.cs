namespace Customers.Application.Tenants;

/// <summary>An operator approval cannot be accepted for the current tenant paid period.</summary>
/// <param name="message">The non-secret reason for the conflict.</param>
public sealed class TenantPurgeApprovalConflictException(string message) : Exception(message);

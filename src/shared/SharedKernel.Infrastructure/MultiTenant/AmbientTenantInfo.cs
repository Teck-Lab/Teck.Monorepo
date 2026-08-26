using Finbuckle.MultiTenant.Abstractions;

namespace SharedKernel.Infrastructure.MultiTenant;

/// <summary>
/// Exposes the tenant that is active when an application handler uses it.
/// </summary>
/// <remarks>
/// Wolverine creates handler dependencies before its incoming-message middleware runs. Keeping
/// this view scoped but resolving its backing tenant per property access lets
/// <c>TenantPropagationMiddleware</c> establish an envelope tenant after dependency creation.
/// </remarks>
internal sealed class AmbientTenantInfo(
    IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor) : ITenantInfo
{
    /// <inheritdoc />
    public string Id => Current.Id;

    /// <inheritdoc />
    public string Identifier => Current.Identifier;

    private ITenantInfo Current => tenantContextAccessor.MultiTenantContext?.TenantInfo
        ?? throw new InvalidOperationException(
            "No tenant is active for this operation. Tenant-scoped handlers require an authenticated request or a tenant-bearing message envelope.");
}

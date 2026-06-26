namespace SharedKernel.Infrastructure.Messaging.MultiTenant;

/// <summary>
/// Provides ambient, asynchronous-flow access to the tenant identifier currently being propagated
/// across the messaging pipeline.
/// </summary>
public static class TenantPropagationContext
{
    private static readonly AsyncLocal<string?> CurrentTenantIdHolder = new();

    /// <summary>
    /// Gets or sets the tenant identifier associated with the current asynchronous execution flow.
    /// </summary>
    public static string? CurrentTenantId
    {
        get => CurrentTenantIdHolder.Value;
        set => CurrentTenantIdHolder.Value = value;
    }
}

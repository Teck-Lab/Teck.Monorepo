using System.Threading;

namespace SharedKernel.Infrastructure.Messaging.MultiTenant;

public static class TenantPropagationContext
{
    private static readonly AsyncLocal<string?> CurrentTenantIdHolder = new();

    public static string? CurrentTenantId
    {
        get => CurrentTenantIdHolder.Value;
        set => CurrentTenantIdHolder.Value = value;
    }
}

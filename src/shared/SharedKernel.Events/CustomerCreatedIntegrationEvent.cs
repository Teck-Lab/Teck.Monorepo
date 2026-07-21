using MemoryPack;
using SharedKernel.Core.Events;

namespace SharedKernel.Events;

/// <summary>
/// Integration event published when a new customer has been created. Consumed by services
/// that need to initialize customer-related state.
/// </summary>
[MemoryPackable]
public partial class CustomerCreatedIntegrationEvent : IntegrationEvent
{
    /// <summary>Initializes a new instance of the <see cref="CustomerCreatedIntegrationEvent"/> class.</summary>
    [MemoryPackConstructor]
    public CustomerCreatedIntegrationEvent()
    {
    }

    /// <summary>Gets or sets the customer identifier.</summary>
    public Guid CustomerId { get; set; }

    /// <summary>Gets or sets the tenant identifier.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the Keycloak subject identifier.</summary>
    public string KeycloakSubjectId { get; set; } = string.Empty;

    /// <summary>Gets or sets the customer email address.</summary>
    public string Email { get; set; } = string.Empty;
}

using SharedKernel.Core.Domain;

namespace Notifications.Domain.Entities;

/// <summary>Tenant-scoped projection of a shopper contact used exclusively for notifications.</summary>
public sealed class CustomerContact : BaseEntity, IAggregateRoot, ITenantScoped
{
    private CustomerContact() { }
    /// <inheritdoc />
    public string TenantId { get; set; } = string.Empty;
    /// <summary>Gets the customer identifier.</summary>
    public Guid CustomerId { get; private set; }
    /// <summary>Gets the immutable identity subject.</summary>
    public string KeycloakSubjectId { get; private set; } = string.Empty;
    /// <summary>Gets the customer email address.</summary>
    public string Email { get; private set; } = string.Empty;
    /// <summary>Creates a contact projection.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="customerId">The customer identifier.</param>
    /// <param name="subject">The immutable subject.</param>
    /// <param name="email">The recipient email.</param>
    /// <returns>The new contact projection.</returns>
    public static CustomerContact Create(string tenantId, Guid customerId, string subject, string email) => new() { TenantId = tenantId, CustomerId = customerId, KeycloakSubjectId = subject, Email = email };
    /// <summary>Updates contact fields from an authoritative customer event.</summary>
    /// <param name="subject">The immutable subject.</param>
    /// <param name="email">The recipient email.</param>
    public void Update(string subject, string email) { KeycloakSubjectId = subject; Email = email; }
}

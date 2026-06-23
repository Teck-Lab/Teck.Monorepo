using SharedKernel.Core.Domain;

namespace Catalog.Domain.Entities;

/// <summary>
/// A supplier that sources one or more product variants.
/// </summary>
public sealed class Supplier : BaseEntity, IAggregateRoot, ITenantScoped
{
    private Supplier()
    {
    }

    /// <inheritdoc/>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets the supplier name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Gets the contact email.</summary>
    public string? ContactEmail { get; private set; }

    /// <summary>Gets the contact phone.</summary>
    public string? ContactPhone { get; private set; }

    /// <summary>Gets a value indicating whether the supplier is active.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Creates a new active supplier.</summary>
    public static Supplier Create(string tenantId, string name, string? contactEmail = null, string? contactPhone = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        return new Supplier
        {
            TenantId = tenantId,
            Name = name,
            ContactEmail = contactEmail,
            ContactPhone = contactPhone,
            IsActive = true,
        };
    }

    /// <summary>Deactivates the supplier.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Activates the supplier.</summary>
    public void Activate() => IsActive = true;
}

using SharedKernel.Core.Domain;

namespace Catalog.Domain.Entities;

/// <summary>
/// A grouping of products. Supports a simple parent/child hierarchy.
/// </summary>
public sealed class Category : BaseEntity, IAggregateRoot, ITenantScoped
{
    private Category()
    {
    }

    /// <inheritdoc/>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets the display name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Gets the URL slug.</summary>
    public string Slug { get; private set; } = string.Empty;

    /// <summary>Gets the optional parent category id.</summary>
    public Guid? ParentId { get; private set; }

    /// <summary>Creates a new category.</summary>
    /// <param name="tenantId">The owning tenant id.</param>
    /// <param name="name">The display name.</param>
    /// <param name="slug">The URL slug.</param>
    /// <param name="parentId">The optional parent category id.</param>
    /// <returns>The newly created category.</returns>
    public static Category Create(string tenantId, string name, string slug, Guid? parentId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("Slug is required.", nameof(slug));
        }

        return new Category
        {
            TenantId = tenantId,
            Name = name,
            Slug = slug,
            ParentId = parentId,
        };
    }

    /// <summary>Renames the category.</summary>
    /// <param name="name">The new display name.</param>
    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        Name = name;
    }
}

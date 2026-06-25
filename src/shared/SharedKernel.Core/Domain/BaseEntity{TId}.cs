using System.Text.Json.Serialization;
using SharedKernel.Core.Events;

namespace SharedKernel.Core.Domain;

/// <summary>
/// The base entity.
/// </summary>
/// <typeparam name="TId">The type of the entity's identifier.</typeparam>
public abstract class BaseEntity<TId> : IBaseEntity<TId>
{
    /// <summary>
    /// The domain events.
    /// </summary>
    [JsonIgnore]
    private readonly List<IDomainEvent> _domainEvents = new();

    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    [JsonPropertyOrder(-1)]
    public TId Id { get; protected set; } = default!;

    /// <summary>
    /// Gets the created on.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the created by.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CreatedBy { get; private set; }

    /// <summary>
    /// Gets the updated on.
    /// </summary>
    public DateTimeOffset? UpdatedOn { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the updated by.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UpdatedBy { get; private set; }

    /// <summary>
    /// Gets the deleted on.
    /// </summary>
    public DateTimeOffset? DeletedOn { get; private set; }

    /// <summary>
    /// Gets the deleted by.
    /// </summary>
    public string? DeletedBy { get; private set; }

    /// <summary>
    /// Gets a value indicating whether deleted.
    /// </summary>
    public bool IsDeleted { get; private set; }

    /// <summary>
    /// Gets the domain events associated with the entity.
    /// </summary>
    public IReadOnlyList<IDomainEvent> DomainEvents => [.. _domainEvents];

    /// <summary>
    /// Set deleted properties.
    /// </summary>
    /// <param name="isDeleted">If true, is deleted.</param>
    /// <param name="deletedBy">The deleted by.</param>
    public void SetDeletedProperties(bool isDeleted, string? deletedBy)
    {
        if (isDeleted)
        {
            IsDeleted = isDeleted;
            DeletedOn = DateTimeOffset.UtcNow;
            DeletedBy = deletedBy;
        }
        else
        {
            IsDeleted = isDeleted;
            DeletedOn = null;
            DeletedBy = null;
        }
    }

    /// <summary>
    /// Set created by properties.
    /// </summary>
    /// <param name="createdBy">The created by.</param>
    public void SetCreatedByProperties(string? createdBy)
    {
        CreatedBy ??= createdBy;
    }

    /// <summary>
    /// Set updated properties.
    /// </summary>
    /// <param name="updatedOn">The updated on.</param>
    /// <param name="updatedBy">The updated by.</param>
    public void SetUpdatedProperties(DateTimeOffset? updatedOn, string? updatedBy)
    {
        UpdatedOn = updatedOn;
        UpdatedBy = updatedBy;
    }

    /// <summary>
    /// Add domain event.
    /// </summary>
    /// <param name="event">The event.</param>
    public void AddDomainEvent(IDomainEvent @event)
    {
        _domainEvents.Add(@event);
    }

    /// <summary>
    /// Clear domain events.
    /// </summary>
    /// <returns>An array of IDomainEvents.</returns>
    public IDomainEvent[] ClearDomainEvents()
    {
        IDomainEvent[] dequeuedEvents = [.. _domainEvents];
        _domainEvents.Clear();
        return dequeuedEvents;
    }
}

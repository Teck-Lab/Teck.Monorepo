namespace SharedKernel.Core.Domain;

/// <summary>
/// Interface for marking entities as softdeleteable.
/// </summary>
public interface ISoftDeletable
{
    /// <summary>
    /// Gets date of the deletion.
    /// </summary>
    DateTimeOffset? DeletedOn { get; }

    /// <summary>
    /// Gets who deleted the entity.
    /// </summary>
    string? DeletedBy { get; }

    /// <summary>
    /// Gets a value indicating whether gets if entity is deleted.
    /// </summary>
    bool IsDeleted { get; }

    /// <summary>
    /// Sets the deleted properties.
    /// </summary>
    /// <param name="isDeleted">A value indicating whether the entity is deleted.</param>
    /// <param name="deletedBy">The identifier of the user who deleted the entity.</param>
    void SetDeletedProperties(bool isDeleted, string? deletedBy);
}

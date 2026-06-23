namespace SharedKernel.Core.Domain
{
    /// <summary>
    /// Base class for read models used in CQRS pattern.
    /// </summary>
    /// <typeparam name="TId">The type of the ID.</typeparam>
    public abstract class ReadModelBase<TId> : IReadModel<TId>
    {
        /// <summary>
        /// Gets or sets the ID of the read model.
        /// </summary>
        /// <remarks>
        /// Read models have public setters as they are DTOs used for querying and data transfer.
        /// Unlike domain entities, they don't enforce invariants through constructors or factory methods.
        /// </remarks>
        public TId Id { get; set; } = default!;

        /// <summary>
        /// Gets or sets the creation date.
        /// </summary>
        public DateTimeOffset CreatedAt { get; set; }

        /// <summary>
        /// Gets or sets the user who created the entity.
        /// </summary>
        public string? CreatedBy { get; set; }

        /// <summary>
        /// Gets or sets the last update date.
        /// </summary>
        public DateTimeOffset? UpdatedOn { get; set; }

        /// <summary>
        /// Gets or sets the user who last updated the entity.
        /// </summary>
        public string? UpdatedBy { get; set; }

        /// <summary>
        /// Gets or sets the deletion date.
        /// </summary>
        public DateTimeOffset? DeletedOn { get; set; }

        /// <summary>
        /// Gets or sets the user who deleted the entity.
        /// </summary>
        public string? DeletedBy { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the entity is deleted.
        /// </summary>
        public bool IsDeleted { get; set; }
    }
}

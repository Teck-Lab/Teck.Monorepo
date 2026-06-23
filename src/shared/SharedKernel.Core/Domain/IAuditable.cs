namespace SharedKernel.Core.Domain
{
    /// <summary>
    /// Interface for marking entity auditable.
    /// </summary>
    public interface IAuditable
    {
        /// <summary>
        /// Gets date of creation.
        /// </summary>
        DateTimeOffset CreatedAt { get; }

        /// <summary>
        /// Gets who created it.
        /// </summary>
        string? CreatedBy { get; }

        /// <summary>
        /// Gets date of update.
        /// </summary>
        DateTimeOffset? UpdatedOn { get; }

        /// <summary>
        /// Gets who updated it.
        /// </summary>
        string? UpdatedBy { get; }

        /// <summary>
        /// Set updated properties.
        /// </summary>
        /// <param name="updatedOn"></param>
        /// <param name="updatedBy"></param>
        void SetUpdatedProperties(DateTimeOffset? updatedOn, string? updatedBy);

        /// <summary>
        /// Set created properties.
        /// </summary>
        /// <param name="createdBy"></param>
        void SetCreatedByProperties(string? createdBy);
    }
}

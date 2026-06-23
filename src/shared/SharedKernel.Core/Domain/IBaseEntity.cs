namespace SharedKernel.Core.Domain
{
    /// <summary>
    /// Base entity interface.
    /// </summary>
    public interface IBaseEntity
    {
    }

    /// <summary>
    /// Base entity interface with softdelete and audit.
    /// </summary>
    /// <typeparam name="TId"></typeparam>
    public interface IBaseEntity<out TId> : IBaseEntity, ISoftDeletable, IAuditable
    {
        /// <summary>
        /// Gets the id.
        /// </summary>
        TId Id { get; }
    }
}

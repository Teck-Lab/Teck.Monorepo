namespace SharedKernel.Core.Domain;

/// <summary>
/// Base entity interface.
/// </summary>
public interface IBaseEntity
{
}

/// <summary>
/// Base entity interface with softdelete and audit.
/// </summary>
/// <typeparam name="TId">The type of the entity's identifier.</typeparam>
public interface IBaseEntity<out TId> : IBaseEntity, ISoftDeletable, IAuditable, IReadModel<TId>
{
}

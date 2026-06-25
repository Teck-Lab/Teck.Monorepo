namespace SharedKernel.Core.Domain;

/// <summary>
/// Interface for read models used in CQRS pattern.
/// </summary>
/// <typeparam name="TId">The type of the ID.</typeparam>
public interface IReadModel<out TId>
{
    /// <summary>
    /// Gets the ID of the read model.
    /// </summary>
    TId Id { get; }
}

namespace SharedKernel.Core.Events;

/// <summary>
/// Base event interface.
/// </summary>
public interface IEvent
{
    /// <summary>
    /// Gets id.
    /// </summary>
    DefaultIdType Id { get; }

    /// <summary>
    /// Gets creation date.
    /// </summary>
    DateTime CreatedOn { get; }

    /// <summary>
    /// Gets metadata.
    /// </summary>
    IDictionary<string, object> MetaData { get; }
}

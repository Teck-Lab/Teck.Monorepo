namespace SharedKernel.Core.Events;

/// <summary>
/// The integration event.
/// </summary>
public class IntegrationEvent : IIntegrationEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IntegrationEvent"/> class.
    /// </summary>
    protected IntegrationEvent()
    {
        Id = DefaultIdType.NewGuid();
        CreatedOn = DateTime.UtcNow;
        MetaData = new Dictionary<string, object>();
    }

    /// <summary>
    /// Gets the id.
    /// </summary>
    public DefaultIdType Id { get; }

    /// <summary>
    /// Gets the creation date.
    /// </summary>
    public DateTime CreatedOn { get; }

    /// <summary>
    /// Gets the meta data.
    /// </summary>
    public IDictionary<string, object> MetaData { get; }
}

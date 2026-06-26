using System.Text.Json.Serialization;
using MassTransit;
using SharedKernel.Core.Events;

namespace SharedKernel.Core.Domain;

/// <summary>
/// The base entity.
/// </summary>
public abstract class BaseEntity : BaseEntity<DefaultIdType>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BaseEntity"/> class.
    /// </summary>
    protected BaseEntity() => Id = NewId.Next().ToGuid();
}

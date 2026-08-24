using Notifications.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace Notifications.Application.Notifications.Mapping;

/// <summary>Compile-time mapping definitions for notification read models.</summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class NotificationMapper
{
    /// <summary>Maps a delivery to its immutable transport representation.</summary>
    /// <param name="delivery">The persisted delivery to represent.</param>
    /// <returns>The delivery fields needed by callers of the application layer.</returns>
    public static partial NotificationDeliveryModel ToModel(NotificationDelivery delivery);
}

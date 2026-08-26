namespace Notifications.Application.Notifications;

/// <summary>Options controlling the deterministic notification service defaults.</summary>
public sealed class NotificationOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Notification";
    /// <summary>Gets the label used by the deterministic transport.</summary>
    public string SenderName { get; init; } = "Teck";
}

namespace SharedKernel.Events;

/// <summary>
/// Integration message describing an audited change to a domain entity.
/// Published via Wolverine by <c>SharedKernel.Infrastructure.Database.Auditing.AuditPublisher</c>.
/// </summary>
/// <param name="EntityName">The audited entity type name.</param>
/// <param name="EntityId">The audited entity identifier.</param>
/// <param name="Action">The change action (e.g. Created, Updated, Deleted).</param>
/// <param name="TenantId">The owning tenant identifier.</param>
/// <param name="OccurredAt">When the change occurred (UTC).</param>
public sealed record AuditEvent(
    string EntityName,
    string EntityId,
    string Action,
    string TenantId,
    DateTimeOffset OccurredAt);

using Microsoft.EntityFrameworkCore;
using Notifications.Application.Notifications;
using Notifications.Domain.Entities;

namespace Notifications.Application.Database;

/// <summary>Application-owned persistence boundary for durable stub email acceptances.</summary>
/// <param name="dbContext">The scoped notification write context shared with delivery updates.</param>
public sealed class StubEmailAcceptanceDbContextStore(NotificationDbContext dbContext) : IStubEmailAcceptanceStore
{
    /// <inheritdoc />
    public async Task RecordAsync(EmailMessage message, string tenantId, string idempotencyKey, CancellationToken ct)
    {
        if (!await HasAcceptedAsync(tenantId, idempotencyKey, ct).ConfigureAwait(false))
        {
            await dbContext.StubEmailAcceptances.AddAsync(StubEmailAcceptance.Create(tenantId, idempotencyKey, message.Recipient, message.Subject, message.Body), ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task<bool> HasAcceptedAsync(string tenantId, string idempotencyKey, CancellationToken ct) =>
        dbContext.StubEmailAcceptances
            .AsNoTracking()
            .AnyAsync(receipt => receipt.TenantId == tenantId && receipt.IdempotencyKey == idempotencyKey, ct);
}

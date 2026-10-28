using System.Buffers.Binary;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Application.Database;

/// <summary>EF-backed license store with subscription idempotency.</summary>
public sealed class LicenseDbStore : ILicenseStore
{
    private readonly LicensingDbContext db;

    /// <summary>Creates an EF-backed license store.</summary>
    /// <param name="db">The licensing database context.</param>
    public LicenseDbStore(LicensingDbContext db) => this.db = db;

    /// <inheritdoc />
    public async Task<IssuedLicense?> FindBySubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken) =>
        await db.Licenses.AsNoTracking().Where(x => x.SubscriptionId == subscriptionId)
            .OrderBy(x => x.Revoked).ThenByDescending(x => x.IssuedAt)
            .FirstOrDefaultAsync(cancellationToken) is { } row ? row.ToIssuedLicense() : null;

    /// <inheritdoc />
    public async Task<IssuedLicense?> FindByLicenseIdAsync(Guid licenseId, CancellationToken cancellationToken) =>
        await db.Licenses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == licenseId, cancellationToken) is { } row ? row.ToIssuedLicense() : null;

    /// <inheritdoc />
    public Task<bool> IsSuspendedAsync(Guid sourceOrderId, Guid subscriptionId, CancellationToken cancellationToken) =>
        db.SubscriptionSuspensions.AsNoTracking().AnyAsync(row => row.SourceOrderId == sourceOrderId &&
            row.SubscriptionId == subscriptionId, cancellationToken);

    /// <inheritdoc />
    public async Task AddUnlessSuspendedAsync(IssuedLicense license, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockSubscriptionAsync(license.SubscriptionId, cancellationToken);
        if (await IsSuspendedAsync(license.SourceOrderId, license.SubscriptionId, cancellationToken))
        {
            throw new InvalidOperationException("Suspended subscription cannot receive a license.");
        }

        db.Licenses.Add(LicenseRecord.FromIssuedLicense(license));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task SuspendAsync(Guid sourceOrderId, Guid subscriptionId, Guid tenantId, DateTimeOffset suspendedAtUtc, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockSubscriptionAsync(subscriptionId, cancellationToken);
        var prior = await db.SubscriptionSuspensions.SingleOrDefaultAsync(row => row.SourceOrderId == sourceOrderId, cancellationToken);
        if (prior is not null && (prior.SubscriptionId != subscriptionId || prior.TenantId != tenantId))
        {
            throw new InvalidOperationException("Suspension identity differs from the confirmed subscription.");
        }

        var active = await db.Licenses.Where(row => row.SubscriptionId == subscriptionId && !row.Revoked)
            .SingleOrDefaultAsync(cancellationToken);
        if (active is not null && active.TenantId != tenantId)
        {
            throw new InvalidOperationException("Subscription belongs to a different tenant.");
        }
        if (active is not null && active.SourceOrderId != sourceOrderId &&
            !await db.Licenses.AnyAsync(row => row.SourceOrderId == sourceOrderId &&
                row.SubscriptionId == subscriptionId && row.TenantId == tenantId && row.Revoked,
                cancellationToken))
        {
            throw new InvalidOperationException("Suspension does not match a known paid period.");
        }

        if (prior is null)
        {
            db.SubscriptionSuspensions.Add(new SubscriptionSuspensionRecord
            {
                SourceOrderId = sourceOrderId,
                SubscriptionId = subscriptionId,
                TenantId = tenantId,
                SuspendedAtUtc = suspendedAtUtc,
            });
        }

        if (active is not null && active.SourceOrderId == sourceOrderId)
        {
            active.Revoked = true;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(IssuedLicense license, CancellationToken cancellationToken)
    {
        var row = await db.Licenses.SingleAsync(x => x.Id == license.LicenseId, cancellationToken);
        row.Revoked = license.Revoked;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReplaceAsync(Guid licenseId, IssuedLicense replacement, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockSubscriptionAsync(replacement.SubscriptionId, cancellationToken);
        if (await IsSuspendedAsync(replacement.SourceOrderId, replacement.SubscriptionId, cancellationToken))
        {
            throw new InvalidOperationException("Suspended subscription cannot be renewed.");
        }

        var prior = await db.Licenses.SingleAsync(x => x.Id == licenseId, cancellationToken);
        if (prior.SubscriptionId != replacement.SubscriptionId || prior.TenantId != replacement.TenantId)
        {
            throw new InvalidOperationException("Renewal does not match the existing subscription identity.");
        }

        if (await db.Licenses.AnyAsync(row => row.SubscriptionId == replacement.SubscriptionId &&
            !row.Revoked && row.Id != prior.Id, cancellationToken))
        {
            throw new InvalidOperationException("A newer paid period already owns the active subscription license.");
        }

        prior.Revoked = true;
        db.Licenses.Add(LicenseRecord.FromIssuedLicense(replacement));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task LockSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken)
    {
        Span<byte> bytes = stackalloc byte[16];
        subscriptionId.TryWriteBytes(bytes);
        var lockId = BinaryPrimitives.ReadInt64LittleEndian(bytes) ^ BinaryPrimitives.ReadInt64LittleEndian(bytes[8..]);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockId})", cancellationToken);
    }
}

using System.Data;
using Licensing.Application;
using Licensing.Application.Database;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Core.Database;
using Xunit;

namespace Licensing.UnitTests;

public sealed class EnterprisePolicyStoreTests
{
    [Fact]
    public async Task Policy_state_requires_exact_identity_and_reports_validity_and_revocation()
    {
        var options = new DbContextOptionsBuilder<LicensingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new LicensingDbContext(options);
        var unitOfWork = new TestUnitOfWork(db);
        var store = new EnterprisePolicyDbStore(db, unitOfWork);
        var tenant = Guid.NewGuid();
        var subscription = Guid.NewGuid();
        var order = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var policy = TestPolicy(tenant, subscription, order, now.AddHours(-1), now.AddHours(1));
        await store.AddAsync(policy, new EnterprisePolicyAudit(policy.PolicyId, policy.PolicyVersion, "issuer", "issued", now.AddHours(-2)), default).ConfigureAwait(false);

        Assert.Null(await store.GetStateAsync(policy.PolicyId, policy.PolicyVersion, Guid.NewGuid(), subscription, order, now, default));
        Assert.Null(await store.GetStateAsync(policy.PolicyId, policy.PolicyVersion, tenant, Guid.NewGuid(), order, now, default));
        Assert.Null(await store.GetStateAsync(policy.PolicyId, policy.PolicyVersion, tenant, subscription, Guid.NewGuid(), now, default));
        Assert.Equal(EnterprisePolicyStatus.Active, (await store.GetStateAsync(policy.PolicyId, policy.PolicyVersion, tenant, subscription, order, now, default).ConfigureAwait(false))?.Status);
        Assert.True(await store.RevokeAsync(policy.PolicyId, policy.PolicyVersion, "operator", now, default).ConfigureAwait(false));
        Assert.False(await store.RevokeAsync(policy.PolicyId, policy.PolicyVersion, "operator", now, default).ConfigureAwait(false));
        Assert.Equal(EnterprisePolicyStatus.Revoked, (await store.GetStateAsync(policy.PolicyId, policy.PolicyVersion, tenant, subscription, order, now, default).ConfigureAwait(false))?.Status);
        Assert.Equal("operator", (await db.EnterprisePolicyAudit.OrderBy(x => x.Id).LastAsync().ConfigureAwait(false)).ActorSubjectId);
        Assert.Equal(3, unitOfWork.BeginCount);
        Assert.Equal(2, unitOfWork.CommitCount);
        Assert.Equal(1, unitOfWork.RollbackCount);
        Assert.Equal(2, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Policy_state_distinguishes_not_yet_valid_and_expired()
    {
        var options = new DbContextOptionsBuilder<LicensingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new LicensingDbContext(options);
        var store = new EnterprisePolicyDbStore(db, new TestUnitOfWork(db));
        var now = DateTimeOffset.UtcNow;
        var future = TestPolicy(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now.AddHours(1), now.AddHours(2));
        var past = TestPolicy(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now.AddHours(-2), now.AddHours(-1)) with { PolicyId = "expired" };
        await store.AddAsync(future, new EnterprisePolicyAudit(future.PolicyId, future.PolicyVersion, "issuer", "issued", now), default).ConfigureAwait(false);
        await store.AddAsync(past, new EnterprisePolicyAudit(past.PolicyId, past.PolicyVersion, "issuer", "issued", now.AddTicks(1)), default).ConfigureAwait(false);
        Assert.Equal(EnterprisePolicyStatus.NotYetValid, (await store.GetStateAsync(future.PolicyId, future.PolicyVersion, future.TenantId, future.SubscriptionId, future.SourceOrderId, now, default).ConfigureAwait(false))?.Status);
        Assert.Equal(EnterprisePolicyStatus.Expired, (await store.GetStateAsync(past.PolicyId, past.PolicyVersion, past.TenantId, past.SubscriptionId, past.SourceOrderId, now, default).ConfigureAwait(false))?.Status);
    }

    [Fact]
    public async Task Newest_policy_version_supersedes_older_version_for_same_bound_purchase()
    {
        var options = new DbContextOptionsBuilder<LicensingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new LicensingDbContext(options);
        var store = new EnterprisePolicyDbStore(db, new TestUnitOfWork(db));
        var now = DateTimeOffset.UtcNow;
        var policy = TestPolicy(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now.AddHours(-1), now.AddHours(1));
        await store.AddAsync(policy, new EnterprisePolicyAudit(policy.PolicyId, policy.PolicyVersion, "issuer", "issued", now), default).ConfigureAwait(false);
        var next = policy with { PolicyVersion = "2" };
        await store.AddAsync(next, new EnterprisePolicyAudit(next.PolicyId, next.PolicyVersion, "issuer", "issued", now.AddTicks(1)), default).ConfigureAwait(false);
        Assert.Equal(EnterprisePolicyStatus.Superseded, (await store.GetStateAsync(policy.PolicyId, policy.PolicyVersion, policy.TenantId, policy.SubscriptionId, policy.SourceOrderId, now, default).ConfigureAwait(false))?.Status);
        Assert.Equal(EnterprisePolicyStatus.Active, (await store.GetStateAsync(next.PolicyId, next.PolicyVersion, next.TenantId, next.SubscriptionId, next.SourceOrderId, now, default).ConfigureAwait(false))?.Status);
    }

    [Fact]
    public async Task Later_version_for_another_purchase_does_not_supersede_this_policy()
    {
        var options = new DbContextOptionsBuilder<LicensingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new LicensingDbContext(options);
        var store = new EnterprisePolicyDbStore(db, new TestUnitOfWork(db));
        var now = DateTimeOffset.UtcNow;
        var tenant = Guid.NewGuid();
        var subscription = Guid.NewGuid();
        var policy = TestPolicy(tenant, subscription, Guid.NewGuid(), now.AddHours(-1), now.AddHours(1));
        await store.AddAsync(policy, new EnterprisePolicyAudit(policy.PolicyId, policy.PolicyVersion, "issuer", "issued", now), default).ConfigureAwait(false);
        var unrelated = TestPolicy(tenant, subscription, Guid.NewGuid(), now.AddHours(-1), now.AddHours(1)) with
        {
            PolicyId = policy.PolicyId,
            PolicyVersion = "2",
        };
        await store.AddAsync(unrelated, new EnterprisePolicyAudit(unrelated.PolicyId, unrelated.PolicyVersion, "issuer", "issued", now.AddTicks(1)), default).ConfigureAwait(false);
        Assert.Equal(EnterprisePolicyStatus.Active, (await store.GetStateAsync(policy.PolicyId, policy.PolicyVersion, policy.TenantId,
            policy.SubscriptionId, policy.SourceOrderId, now, default).ConfigureAwait(false))?.Status);
    }
    [Fact]
    public async Task Failed_policy_or_audit_save_rolls_back_transaction()
    {
        var options = new DbContextOptionsBuilder<LicensingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new LicensingDbContext(options);
        var unitOfWork = new TestUnitOfWork(db) { FailNextSave = true };
        var store = new EnterprisePolicyDbStore(db, unitOfWork);
        var now = DateTimeOffset.UtcNow;
        var policy = TestPolicy(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now.AddHours(-1), now.AddHours(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAsync(policy,
            new EnterprisePolicyAudit(policy.PolicyId, policy.PolicyVersion, "issuer", "issued", now), default)).ConfigureAwait(false);

        Assert.Equal(1, unitOfWork.BeginCount);
        Assert.Equal(1, unitOfWork.RollbackCount);
        Assert.Equal(0, unitOfWork.CommitCount);
        Assert.Equal(0, await db.EnterprisePolicyAudit.CountAsync().ConfigureAwait(false));
    }

    private static EnterprisePolicyDocument TestPolicy(Guid tenant, Guid subscription, Guid order, DateTimeOffset from, DateTimeOffset until) =>
        new("policy", "1", tenant, subscription, order, 10, 20, from, until, "enterprise", "key", "signature");

    private sealed class TestUnitOfWork(LicensingDbContext context) : IUnitOfWork
    {
        public int BeginCount { get; private set; }

        public int CommitCount { get; private set; }

        public int RollbackCount { get; private set; }

        public int SaveCount { get; private set; }
        public bool FailNextSave { get; init; }

        public Task<IDbTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeginCount++;
            return Task.FromResult<IDbTransaction>(new TestTransaction());
        }

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CommitCount++;
            return Task.CompletedTask;
        }

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RollbackCount++;
            return Task.CompletedTask;
        }

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (FailNextSave)
            {
                throw new InvalidOperationException("Injected persistence failure.");
            }

            return await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
        }

        private sealed class TestTransaction : IDbTransaction
        {
            public IDbConnection? Connection => null;

            public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

            public void Commit()
            {
            }

            public void Rollback()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}

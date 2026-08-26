using SharedKernel.Core.Domain;
using Xunit;

namespace SharedKernel.UnitTests.Domain;

/// <summary>
/// Guards the global-uniqueness property of entity identifiers.
/// </summary>
/// <remarks>
/// Several unique indexes on tenant-scoped tables constrain a single identifier column rather
/// than a tenant composite — <c>IX_payments_OrderId</c> is the notable one, enforcing one payment
/// per order. That is only correct because <see cref="BaseEntity"/> assigns a globally unique
/// identifier through MassTransit's Snowflake-derived <c>NewId</c>, so two tenants can never
/// generate the same value. These tests pin that assumption: if identifier generation ever
/// becomes tenant-scoped or otherwise non-unique, they fail here rather than surfacing as
/// legitimate payments being rejected in production.
/// </remarks>
public sealed class EntityIdentityTests
{
    private sealed class TestEntity : BaseEntity;

    [Fact]
    public void NewEntities_ReceiveDistinctIdentifiers()
    {
        const int count = 10_000;

        var ids = new HashSet<Guid>();
        for (var i = 0; i < count; i++)
        {
            Assert.True(ids.Add(new TestEntity().Id), "Entity identifier generation produced a duplicate.");
        }

        Assert.Equal(count, ids.Count);
    }

    [Fact]
    public void NewEntities_ReceiveNonEmptyIdentifiers()
    {
        Assert.NotEqual(Guid.Empty, new TestEntity().Id);
    }

    [Fact]
    public async Task ConcurrentEntityCreation_ProducesNoDuplicateIdentifiers()
    {
        const int perTask = 2_000;
        const int tasks = 8;

        IEnumerable<Task<Guid[]>> work = Enumerable.Range(0, tasks).Select(_ => Task.Run(() =>
            Enumerable.Range(0, perTask).Select(_ => new TestEntity().Id).ToArray()));

        Guid[][] batches = await Task.WhenAll(work);
        Guid[] all = [.. batches.SelectMany(batch => batch)];

        Assert.Equal(all.Length, all.Distinct().Count());
    }
}

using Billings.Application.Database;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.Extensions;
using SharedKernel.Core.Database;
using SharedKernel.Core.Domain;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Billing.UnitTests.TestContext;

/// <summary>Builds <see cref="BillingDbContext"/> instances over the EF Core InMemory provider for handler tests.</summary>
public static class BillingTestContext
{
    private static DbContextOptions<BillingDbContext> Options(string? name) =>
        new DbContextOptionsBuilder<BillingDbContext>()
            .UseInMemoryDatabase(name ?? $"billing-{Guid.NewGuid()}")
            .Options;

    private static IMultiTenantContextAccessor<TenantDetails> NoTenant() =>
        // The substituted accessor resolves no tenant, so the context's TenantId is null.
        // Production contexts always receive a real tenant from the Host's interceptor.
        Substitute.For<IMultiTenantContextAccessor<TenantDetails>>();

    /// <summary>Creates an isolated in-memory billing context (real SaveChanges).
    /// Use for seeding and for pure-create handlers that do a single insert.</summary>
    public static BillingDbContext CreateInMemory(string? name = null) =>
        new(Options(name), NoTenant());

    /// <summary>
    /// Creates a context over the named in-memory database whose <c>SaveChangesAsync</c> is stubbed
    /// (returns 1 without persisting). Use as the ACT context for load-then-mutate handler tests:
    /// seed with <see cref="CreateInMemory(string)"/> using the same <paramref name="name"/>, then run
    /// the handler against this context. This mirrors the catalog service's handler unit tests, which
    /// also stub SaveChanges.
    /// </summary>
    public static BillingDbContext CreateWithStubbedSave(string name)
    {
        var ctx = Substitute.ForPartsOf<BillingDbContext>(Options(name), NoTenant());
        ctx.Configure().SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
        return ctx;
    }

    /// <summary>Builds a write repository over the given context (audit accessor is a no-op substitute).</summary>
    public static IGenericWriteRepository<TEntity, Guid> WriteRepo<TEntity>(BillingDbContext db)
        where TEntity : BaseEntity =>
        new GenericWriteRepository<TEntity, Guid, BillingDbContext>(db, Substitute.For<IHttpContextAccessor>());

    /// <summary>Builds a unit of work that commits the given context.</summary>
    public static IUnitOfWork UnitOfWork(BillingDbContext db) =>
        new UnitOfWork<BillingDbContext>(db);
}

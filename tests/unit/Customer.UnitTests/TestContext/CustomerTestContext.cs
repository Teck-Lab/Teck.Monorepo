using Customers.Application.Database;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.Extensions;
using SharedKernel.Core.Database;
using SharedKernel.Core.Domain;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Customer.UnitTests.TestContext;

/// <summary>Builds <see cref="CustomerDbContext"/> instances over the EF Core InMemory provider for handler tests.</summary>
public static class CustomerTestContext
{
    private static DbContextOptions<CustomerDbContext> Options(string? name) =>
        new DbContextOptionsBuilder<CustomerDbContext>()
            .UseInMemoryDatabase(name ?? $"customer-{Guid.NewGuid()}")
            .Options;

    private static IMultiTenantContextAccessor<TenantDetails> NoTenant() =>
        // The substituted accessor resolves no tenant, so the context's TenantId is null.
        // Seeding and saving the Customer aggregate works in this no-tenant configuration under
        // the InMemory provider — which is what unit tests need. Production contexts always
        // receive a real tenant from the Host's interceptor.
        Substitute.For<IMultiTenantContextAccessor<TenantDetails>>();

    /// <summary>Creates an isolated in-memory customer context (real SaveChanges).
    /// Use for seeding and for pure-create handlers that do a single insert.</summary>
    public static CustomerDbContext CreateInMemory(string? name = null) =>
        new(Options(name), NoTenant());

    /// <summary>
    /// Creates a context over the named in-memory database whose <c>SaveChangesAsync</c> is stubbed
    /// (returns 1 without persisting). Use as the ACT context for load-then-mutate handler tests:
    /// seed with <see cref="CreateInMemory(string)"/> using the same <paramref name="name"/>, then run
    /// the handler against this context. The EF Core InMemory provider cannot persist mutations to an
    /// already-saved owned-aggregate graph, so the mutation is asserted on the loaded aggregate / returned
    /// DTO and the real DB round-trip is covered by Plan 3 Testcontainers integration tests. This mirrors
    /// the order service's handler unit tests, which also stub SaveChanges.
    /// </summary>
    public static CustomerDbContext CreateWithStubbedSave(string name)
    {
        var ctx = Substitute.ForPartsOf<CustomerDbContext>(Options(name), NoTenant());
        ctx.Configure().SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
        return ctx;
    }

    /// <summary>Builds a write repository over the given context (audit accessor is a no-op substitute).</summary>
    public static IGenericWriteRepository<TEntity, Guid> WriteRepo<TEntity>(CustomerDbContext db)
        where TEntity : BaseEntity =>
        new GenericWriteRepository<TEntity, Guid, CustomerDbContext>(db, Substitute.For<IHttpContextAccessor>());

    /// <summary>Builds a unit of work that commits the given context.</summary>
    public static IUnitOfWork UnitOfWork(CustomerDbContext db) =>
        new UnitOfWork<CustomerDbContext>(db);
}

using Catalog.Application.Database;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.Extensions;
using SharedKernel.Core.Database;
using SharedKernel.Core.Domain;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Catalog.UnitTests.TestContext;

/// <summary>Builds <see cref="CatalogDbContext"/> instances over the EF Core InMemory provider for handler tests.</summary>
public static class CatalogTestContext
{
    private static DbContextOptions<CatalogDbContext> Options(string? name) =>
        new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(name ?? $"catalog-{Guid.NewGuid()}")
            .Options;

    private static IMultiTenantContextAccessor<TenantDetails> Tenant()
    {
        var accessor = Substitute.For<IMultiTenantContextAccessor<TenantDetails>>();
        accessor.MultiTenantContext.Returns(new MultiTenantContext<TenantDetails>(new TenantDetails
        {
            Id = "tenant-1",
            Identifier = "tenant-1",
            Name = "Tenant 1",
            IsActive = true,
        }));
        return accessor;
    }

    /// <summary>Creates an isolated in-memory catalog context (real SaveChanges).
    /// Use for seeding and for pure-create handlers that do a single insert.</summary>
    public static CatalogDbContext CreateInMemory(string? name = null) =>
        new(Options(name), Tenant());

    /// <summary>
    /// Creates a context over the named in-memory database whose <c>SaveChangesAsync</c> is stubbed
    /// (returns 1 without persisting). Use as the ACT context for load-then-mutate handler tests:
    /// seed with <see cref="CreateInMemory(string)"/> using the same <paramref name="name"/>, then run
    /// the handler against this context. The EF Core InMemory provider cannot persist mutations to an
    /// already-saved owned-aggregate graph, so the mutation is asserted on the loaded aggregate / returned
    /// DTO and the real DB round-trip is covered by Plan 3 Testcontainers integration tests. This mirrors
    /// the order service's handler unit tests, which also stub SaveChanges.
    /// </summary>
    public static CatalogDbContext CreateWithStubbedSave(string name)
    {
        var ctx = Substitute.ForPartsOf<CatalogDbContext>(Options(name), Tenant());
        ctx.Configure().SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
        return ctx;
    }

    /// <summary>Builds a write repository over the given context (audit accessor is a no-op substitute).</summary>
    public static IGenericWriteRepository<TEntity, Guid> WriteRepo<TEntity>(CatalogDbContext db)
        where TEntity : BaseEntity =>
        new GenericWriteRepository<TEntity, Guid, CatalogDbContext>(db, Substitute.For<IHttpContextAccessor>());

    /// <summary>Builds a unit of work that commits the given context.</summary>
    public static IUnitOfWork UnitOfWork(CatalogDbContext db) =>
        new UnitOfWork<CatalogDbContext>(db);
}

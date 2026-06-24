using Catalog.Application.Database;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SharedKernel.Infrastructure.MultiTenant;

namespace Catalog.UnitTests.TestContext;

/// <summary>Builds a real <see cref="CatalogDbContext"/> over the EF Core InMemory provider for handler tests.</summary>
public static class CatalogTestContext
{
    /// <summary>Creates an isolated in-memory catalog context.</summary>
    public static CatalogDbContext CreateInMemory(string? name = null)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(name ?? $"catalog-{Guid.NewGuid()}")
            .Options;

        // BaseDbContext reads tenantAccessor?.MultiTenantContext.TenantInfo; NSubstitute's recursive
        // mocking returns a non-null context with a null TenantInfo, so TenantDetails resolves to null.
        // SaveChangesAsync's tenant enforcement is a no-op for entities not marked with Finbuckle's
        // [MultiTenant] attribute, so seeding and saving in tests works without a real tenant.
        var accessor = Substitute.For<IMultiTenantContextAccessor<TenantDetails>>();
        return new CatalogDbContext(options, accessor);
    }
}

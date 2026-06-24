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

        // The substituted accessor resolves no tenant, so the context's TenantId is null.
        // The two CatalogDbContextTests confirm that seeding and saving the Product aggregate
        // works in this no-tenant configuration under the InMemory provider — which is what unit
        // tests need. Production contexts always receive a real tenant from the Host's interceptor.
        var accessor = Substitute.For<IMultiTenantContextAccessor<TenantDetails>>();
        return new CatalogDbContext(options, accessor);
    }
}

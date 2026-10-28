using Licensing.Application;
using Licensing.Application.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Licensing.UnitTests;

public sealed class LicensingPersistenceTests
{
    [Fact]
    public void Licensing_context_has_unique_active_subscription_index_and_host_migration()
    {
        var options = new DbContextOptionsBuilder<LicensingDbContext>()
            .UseNpgsql("Host=localhost;Database=licensing;Username=test;Password=test",
                npgsql => npgsql.MigrationsAssembly("Licensing.Host"))
            .Options;
        using var context = new LicensingDbContext(options);

        var entity = context.Model.FindEntityType(typeof(LicenseRecord));
        Assert.NotNull(entity);
        var index = Assert.Single(entity.GetIndexes(), candidate =>
            candidate.Properties.Select(property => property.Name).SequenceEqual([nameof(LicenseRecord.SubscriptionId)]));
        Assert.True(index.IsUnique);
        Assert.Equal("NOT \"Revoked\"", index.GetFilter());

        var migrations = context.GetService<IMigrationsAssembly>().Migrations;
        Assert.Contains(migrations.Keys, name => name.EndsWith("_InitialLicensing", StringComparison.Ordinal));
    }
}

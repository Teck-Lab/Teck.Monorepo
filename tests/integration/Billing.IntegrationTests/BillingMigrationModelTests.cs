using Billings.Application.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Billing.IntegrationTests;

/// <summary>Verifies the committed billing migration and model snapshot are synchronized.</summary>
[Collection("SharedTestcontainers")]
public sealed class BillingMigrationModelTests : BillingIntegrationTestBase
{
    /// <summary>Initializes the migration model test.</summary>
    /// <param name="fixture">The shared PostgreSQL fixture.</param>
    public BillingMigrationModelTests(SharedTestcontainersFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task BillingMigration_AppliesWithoutPendingMigrationsOrModelChanges()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        var hasPendingModelChanges = context.Database.HasPendingModelChanges();
        var migrationsAssembly = context.GetService<IMigrationsAssembly>();
        var modelDiffer = context.GetService<IMigrationsModelDiffer>();
        var differences = modelDiffer.GetDifferences(
            migrationsAssembly.ModelSnapshot!.Model.GetRelationalModel(),
            context.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        Assert.False(hasPendingModelChanges, string.Join(Environment.NewLine, differences.Select(difference => difference.GetType().Name)));
    }
}

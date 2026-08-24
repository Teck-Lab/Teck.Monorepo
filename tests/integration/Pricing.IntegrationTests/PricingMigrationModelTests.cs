// <copyright file="PricingMigrationModelTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using Microsoft.EntityFrameworkCore;
using Pricing.Application.Database;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Pricing.IntegrationTests;

/// <summary>Proves the committed Pricing migrations exactly describe the current EF model.</summary>
[Collection("SharedTestcontainers")]
public sealed class PricingMigrationModelTests
{
    private readonly SharedTestcontainersFixture fixture;

    /// <summary>Initializes the migration-model test against the shared fresh PostgreSQL database.</summary>
    public PricingMigrationModelTests(SharedTestcontainersFixture fixture) => this.fixture = fixture;

    /// <summary>Applies every committed migration and verifies no model delta remains.</summary>
    [Fact]
    public async Task CommittedMigrations_MatchCurrentModel()
    {
        string connectionString = await fixture
            .CreateSharedTestDatabaseAsync(typeof(PricingDbContext), "Pricing.Host")
            .ConfigureAwait(false);
        var options = new DbContextOptionsBuilder<PricingDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Pricing.Host"))
            .Options;

        await using var context = new PricingDbContext(options, null!);
        await context.Database.MigrateAsync().ConfigureAwait(false);

        Assert.Empty(await context.Database.GetPendingMigrationsAsync().ConfigureAwait(false));
        Assert.False(context.Database.HasPendingModelChanges());
    }
}

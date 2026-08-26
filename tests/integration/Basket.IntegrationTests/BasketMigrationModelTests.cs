// <copyright file="BasketMigrationModelTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using Baskets.Application.Database;
using Microsoft.EntityFrameworkCore;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Baskets.IntegrationTests;

/// <summary>Proves the committed Basket migrations exactly describe the current EF model.</summary>
[Collection("SharedTestcontainers")]
public sealed class BasketMigrationModelTests
{
    private readonly SharedTestcontainersFixture fixture;

    /// <summary>Initializes the migration-model test against the shared fresh PostgreSQL database.</summary>
    public BasketMigrationModelTests(SharedTestcontainersFixture fixture) => this.fixture = fixture;

    /// <summary>Applies every committed migration and verifies no model delta remains.</summary>
    [Fact]
    public async Task CommittedMigrations_MatchCurrentModel()
    {
        string connectionString = await fixture
            .CreateSharedTestDatabaseAsync(typeof(BasketDbContext), "Basket.Host")
            .ConfigureAwait(false);
        var options = new DbContextOptionsBuilder<BasketDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Basket.Host"))
            .Options;

        await using var context = new BasketDbContext(options, null!);
        await context.Database.MigrateAsync().ConfigureAwait(false);

        Assert.Empty(await context.Database.GetPendingMigrationsAsync().ConfigureAwait(false));
        Assert.False(context.Database.HasPendingModelChanges());
    }
}

// <copyright file="InventoryMigrationModelTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using Inventories.Application.Database;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Inventories.IntegrationTests;

/// <summary>Verifies the committed inventory migration is fully applied and matches the current model.</summary>
[Collection("SharedTestcontainers")]
public sealed class InventoryMigrationModelTests : InventoryIntegrationTestBase
{
    /// <summary>Initializes a new instance of the <see cref="InventoryMigrationModelTests"/> class.</summary>
    public InventoryMigrationModelTests(SharedTestcontainersFixture fixture)
        : base(fixture)
    {
    }

    /// <summary>Applies the committed migration through the shared fixture and observes no pending migration or model change.</summary>
    [Fact]
    public async Task InventoryMigration_AppliesWithoutPendingMigrationOrModelChanges()
    {
        using IServiceScope scope = Services.CreateScope();
        InventoryDbContext db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    /// <summary>Guards against a migration designer that drops existing entity mappings from its target model.</summary>
    [Fact]
    public void BoundOrderBackorders_TargetModelContainsEveryRuntimeEntityAndOwnedMapping()
    {
        using IServiceScope scope = Services.CreateScope();
        InventoryDbContext db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Type migrationType = db.GetService<IMigrationsAssembly>().Assembly.GetType(
            "Inventories.Host.Database.Migrations.BoundOrderBackorders",
            throwOnError: true)!;
        var migration = (Migration)Activator.CreateInstance(migrationType, nonPublic: true)!;

        IModel target = migration.TargetModel;
        foreach (IEntityType runtimeEntity in db.Model.GetEntityTypes().Where(entity =>
                     entity.Name.StartsWith("Inventories.Domain.", StringComparison.Ordinal) &&
                     !entity.IsOwned()))
        {
            IEntityType? targetEntity = target.FindEntityType(runtimeEntity.Name);
            Assert.True(targetEntity is not null, $"Missing {runtimeEntity.Name}; target entities: {string.Join(", ", target.GetEntityTypes().Select(entity => entity.Name))}");
            Assert.Equal(runtimeEntity.GetTableName(), targetEntity!.GetTableName());
            Assert.Equal(
                runtimeEntity.GetProperties().Select(property => property.Name).Order(),
                targetEntity.GetProperties().Select(property => property.Name).Order());
            Assert.Equal(
                runtimeEntity.GetIndexes().Select(index => string.Join(',', index.Properties.Select(property => property.Name))).Order(),
                targetEntity.GetIndexes().Select(index => string.Join(',', index.Properties.Select(property => property.Name))).Order());
        }

        IEntityType runtimeLine = Assert.Single(db.Model.GetEntityTypes().Where(entity => entity.IsOwned() && entity.ClrType.Name == "ReservationLine"));
        Assert.Contains(target.GetEntityTypes(), entity =>
            entity.IsOwned() &&
            entity.GetTableName() == runtimeLine.GetTableName() &&
            entity.GetProperties().Select(property => property.Name).Order()
                .SequenceEqual(runtimeLine.GetProperties().Select(property => property.Name).Order()));
    }
}

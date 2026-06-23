// <copyright file="SharedTestcontainersFixture.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

namespace Teck.Platform.IntegrationTests.Shared;

/// <summary>
/// Shared fixture that spins up one PostgreSQL and one RabbitMQ container per test project.
/// All tests in the project share the same database, but tables are truncated between tests for isolation.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "xUnit requires collection fixtures to be public")]
public sealed class SharedTestcontainersFixture : IAsyncLifetime
{
    private PostgreSqlContainer? dbContainer;
    private RabbitMqContainer? rabbitMqContainer;
    private readonly SemaphoreSlim databaseLock = new(1, 1);

    /// <summary>
    /// Gets the PostgreSQL container.
    /// </summary>
    public PostgreSqlContainer DbContainer
    {
        get
        {
            if (dbContainer is null)
            {
                throw new InvalidOperationException(
                    "PostgreSQL container is not available. " +
                    "Ensure Docker is running and the fixture InitializeAsync has completed.");
            }

            return dbContainer;
        }
    }

    /// <summary>
    /// Gets the RabbitMQ container.
    /// </summary>
    public RabbitMqContainer RabbitMqContainer
    {
        get
        {
            if (rabbitMqContainer is null)
            {
                throw new InvalidOperationException(
                    "RabbitMQ container is not available. " +
                    "Ensure Docker is running and the fixture InitializeAsync has completed.");
            }

            return rabbitMqContainer;
        }
    }

    /// <summary>
    /// Gets the base PostgreSQL connection string (connects to the default 'postgres' database).
    /// Use this for CREATE DATABASE / DROP DATABASE operations.
    /// </summary>
    public string AdminConnectionString => DbContainer.GetConnectionString();

    /// <summary>
    /// Gets the normalized RabbitMQ connection string (amqp:// format).
    /// </summary>
    public string RabbitMqConnectionString
    {
        get
        {
            string raw = RabbitMqContainer.GetConnectionString();
            if (raw.StartsWith("rabbitmqs://", StringComparison.OrdinalIgnoreCase))
            {
                return string.Concat("amqps://", raw.AsSpan("rabbitmqs://".Length));
            }

            if (raw.StartsWith("rabbitmq://", StringComparison.OrdinalIgnoreCase))
            {
                return string.Concat("amqp://", raw.AsSpan("rabbitmq://".Length));
            }

            return raw;
        }
    }

    /// <summary>
    /// Creates a connection string for a specific test database.
    /// </summary>
    /// <param name="databaseName">The test database name.</param>
    /// <returns>A connection string pointing to the test database.</returns>
    public string GetDatabaseConnectionString(string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Database = databaseName,
            Pooling = true,
            MaxPoolSize = 50,
            ConnectionLifetime = 300,
        };
        return builder.ConnectionString;
    }

    /// <summary>
    /// Creates a single shared test database for the project, runs EF migrations, and returns the connection string.
    /// This database is reused across all tests in the project.
    /// </summary>
    /// <param name="dbContextType">The EF DbContext type to migrate.</param>
    /// <param name="migrationsAssembly">The assembly containing migrations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The connection string for the test database.</returns>
    public async Task<string> CreateSharedTestDatabaseAsync(
        Type dbContextType,
        string migrationsAssembly,
        CancellationToken cancellationToken = default)
    {
        await databaseLock.WaitAsync(cancellationToken);
        try
        {
            string databaseName = $"testdb_{dbContextType.Name.ToLowerInvariant()}";

            // Check if database already exists
            await using var adminConnection = new NpgsqlConnection(AdminConnectionString);
            await adminConnection.OpenAsync(cancellationToken);
            await using var checkCommand = adminConnection.CreateCommand();
            checkCommand.CommandText = $"SELECT 1 FROM pg_database WHERE datname = '{databaseName}'";
            var exists = await checkCommand.ExecuteScalarAsync(cancellationToken) is not null;

            if (!exists)
            {
                await using var createCommand = adminConnection.CreateCommand();
                createCommand.CommandText = $"CREATE DATABASE \"{databaseName}\"";
                await createCommand.ExecuteNonQueryAsync(cancellationToken);

                string testConnectionString = GetDatabaseConnectionString(databaseName);

                // Run migrations using the test database connection
                var optionsBuilderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(dbContextType);
                var optionsBuilder = (DbContextOptionsBuilder)Activator.CreateInstance(optionsBuilderType)!;
                optionsBuilder.UseNpgsql(testConnectionString, npgsql => npgsql.MigrationsAssembly(migrationsAssembly));

                var dbContext = (DbContext)Activator.CreateInstance(dbContextType, optionsBuilder.Options, null)!;
                await using (dbContext)
                {
                    await dbContext.Database.MigrateAsync(cancellationToken);
                }
            }

            return GetDatabaseConnectionString(databaseName);
        }
        finally
        {
            databaseLock.Release();
        }
    }

    /// <summary>
    /// Truncates all tables in the database to ensure test isolation.
    /// Call this in test teardown instead of dropping the database.
    /// </summary>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task TruncateAllTablesAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await databaseLock.WaitAsync(cancellationToken);
        try
        {
            var cleanupConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Pooling = false,
            }.ConnectionString;

            await using var connection = new NpgsqlConnection(cleanupConnectionString);
            await connection.OpenAsync(cancellationToken);

            // Truncate all tables (excluding EF migrations history) in a single command.
            await using var truncateCommand = connection.CreateCommand();
            truncateCommand.CommandText = @"
                DO $$
                DECLARE
                    table_names text;
                BEGIN
                    SELECT string_agg(quote_ident(tablename), ', ')
                    INTO table_names
                    FROM pg_tables
                    WHERE schemaname = 'public'
                    AND tablename != '__EFMigrationsHistory';

                    IF table_names IS NOT NULL THEN
                        EXECUTE 'TRUNCATE TABLE ' || table_names || ' RESTART IDENTITY CASCADE';
                    END IF;
                END $$;";
            await truncateCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            databaseLock.Release();
        }
    }

    /// <summary>
    /// Drops the shared test database. Call this in fixture dispose.
    /// </summary>
    /// <param name="databaseName">The database name to drop.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DropTestDatabaseAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        await using var adminConnection = new NpgsqlConnection(AdminConnectionString);
        await adminConnection.OpenAsync(cancellationToken);

        await using var terminateCommand = adminConnection.CreateCommand();
        terminateCommand.CommandText = $@"
            SELECT pg_terminate_backend(pid) 
            FROM pg_stat_activity 
            WHERE datname = '{databaseName}' 
            AND pid <> pg_backend_pid()";
        await terminateCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var dropCommand = adminConnection.CreateCommand();
        dropCommand.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\"";
        await dropCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheridoc/>
    public async ValueTask InitializeAsync()
    {
        dbContainer = new PostgreSqlBuilder("postgres:latest")
            .WithDatabase("postgres")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithCommand("-c", "max_connections=500")
            .Build();

        rabbitMqContainer = new RabbitMqBuilder("rabbitmq:3-management")
            .WithUsername("guest")
            .WithPassword("guest")
            .Build();

        // Start both containers concurrently
        await Task.WhenAll(
            dbContainer.StartAsync(),
            rabbitMqContainer.StartAsync());
    }

    /// <inheridoc/>
    public async ValueTask DisposeAsync()
    {
        if (rabbitMqContainer is not null)
        {
            try { await rabbitMqContainer.DisposeAsync(); }
            catch { /* best effort */ }
        }

        if (dbContainer is not null)
        {
            try { await dbContainer.DisposeAsync(); }
            catch { /* best effort */ }
        }

        databaseLock.Dispose();
    }
}

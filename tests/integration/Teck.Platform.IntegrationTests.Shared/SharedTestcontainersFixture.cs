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
/// Tests share the containers; database isolation is managed by each test suite.
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
    /// Creates a shared test database, applies any pending EF migrations, and returns its connection string.
    /// </summary>
    public async Task<string> CreateSharedTestDatabaseAsync(
        Type dbContextType,
        string migrationsAssembly,
        CancellationToken cancellationToken = default)
    {
        await databaseLock.WaitAsync(cancellationToken);
        try
        {
            string databaseName = $"testdb_{dbContextType.Name.ToLowerInvariant()}";
            await EnsureDatabaseExistsAsync(databaseName, cancellationToken);
            string connectionString = GetDatabaseConnectionString(databaseName);
            await ApplyMigrationsAsync(dbContextType, migrationsAssembly, connectionString, cancellationToken);
            return connectionString;
        }
        finally
        {
            databaseLock.Release();
        }
    }


    /// <summary>
    /// Creates an isolated test database with a unique name and applies EF migrations.
    /// The returned connection string can be dropped with <see cref="DropTestDatabaseAsync"/>.
    /// </summary>
    public async Task<string> CreateIsolatedTestDatabaseAsync(
        Type dbContextType,
        string migrationsAssembly,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContextType);
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationsAssembly);
        string databaseName = $"testdb_{dbContextType.Name.ToLowerInvariant()}_{Guid.NewGuid():N}";
        await databaseLock.WaitAsync(cancellationToken);
        bool lockHeld = true;
        try
        {
            await EnsureDatabaseExistsAsync(databaseName, cancellationToken);
            string connectionString = GetDatabaseConnectionString(databaseName);
            await ApplyMigrationsAsync(dbContextType, migrationsAssembly, connectionString, cancellationToken);
            return connectionString;
        }
        catch
        {
            databaseLock.Release();
            lockHeld = false;
            try
            {
                await DropTestDatabaseAsync(databaseName, CancellationToken.None);
            }
            catch
            {
                // Preserve the migration/create exception; cleanup is best effort.
            }

            throw;
        }
        finally
        {
            if (lockHeld)
            {
                databaseLock.Release();
            }
        }
    }


    private async Task EnsureDatabaseExistsAsync(string databaseName, CancellationToken cancellationToken)
    {
        await using var adminConnection = new NpgsqlConnection(AdminConnectionString);
        await adminConnection.OpenAsync(cancellationToken);
        await using var checkCommand = adminConnection.CreateCommand();
        checkCommand.CommandText = "SELECT 1 FROM pg_database WHERE datname = @databaseName";
        checkCommand.Parameters.AddWithValue("databaseName", databaseName);
        if (await checkCommand.ExecuteScalarAsync(cancellationToken) is null)
        {
            await using var createCommand = adminConnection.CreateCommand();
            createCommand.CommandText = $"CREATE DATABASE \"{databaseName.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
            await createCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ApplyMigrationsAsync(
        Type dbContextType,
        string migrationsAssembly,
        string connectionString,
        CancellationToken cancellationToken)
    {
        var optionsBuilderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(dbContextType);
        var optionsBuilder = (DbContextOptionsBuilder)Activator.CreateInstance(optionsBuilderType)!;
        optionsBuilder.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(migrationsAssembly));
        var dbContext = (DbContext)Activator.CreateInstance(dbContextType, optionsBuilder.Options, null)!;
        await using (dbContext)
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
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
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        if (databaseName.Length > 63
            || databaseName.Any(character => !char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '_'))
        {
            throw new ArgumentException("Only safe test database identifiers can be dropped.", nameof(databaseName));
        }

        await using var adminConnection = new NpgsqlConnection(AdminConnectionString);
        await adminConnection.OpenAsync(cancellationToken);

        await using var terminateCommand = adminConnection.CreateCommand();
        terminateCommand.CommandText = @"
            SELECT pg_terminate_backend(pid)
            FROM pg_stat_activity
            WHERE datname = @databaseName
            AND pid <> pg_backend_pid()";
        terminateCommand.Parameters.AddWithValue("databaseName", databaseName);
        await terminateCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var dropCommand = adminConnection.CreateCommand();
        dropCommand.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\"";
        await dropCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Drops a test database, clearing pooled connections before terminating its sessions.
    /// </summary>
    /// <param name="connectionString">Connection string for the database to drop.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DropIsolatedTestDatabaseAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        string? databaseName = new NpgsqlConnectionStringBuilder(connectionString).Database;
        int suffixSeparator = databaseName?.LastIndexOf('_') ?? -1;
        if (databaseName is null
            || !databaseName.StartsWith("testdb_", StringComparison.Ordinal)
            || suffixSeparator <= "testdb_".Length
            || !Guid.TryParseExact(databaseName[(suffixSeparator + 1)..], "N", out _))
        {
            throw new ArgumentException("Only isolated test database identifiers can be dropped through this method.", nameof(connectionString));
        }

        await databaseLock.WaitAsync(cancellationToken);
        try
        {
            using var isolatedConnection = new NpgsqlConnection(connectionString);
            NpgsqlConnection.ClearPool(isolatedConnection);
            await DropTestDatabaseAsync(databaseName, cancellationToken);
        }
        finally
        {
            databaseLock.Release();
        }
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

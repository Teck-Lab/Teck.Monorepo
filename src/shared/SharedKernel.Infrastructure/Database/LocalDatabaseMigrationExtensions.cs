using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SharedKernel.Infrastructure.Database;

/// <summary>
/// Shared helpers for applying EF Core migrations in local/development runs.
/// </summary>
public static class LocalDatabaseMigrationExtensions
{
    /// <summary>
    /// Applies pending EF Core migrations for the provided DbContext when running locally.
    /// </summary>
    /// <typeparam name="TContext">The write DbContext type.</typeparam>
    /// <param name="app">The application builder.</param>
    /// <returns>The same application builder instance.</returns>
    public static IApplicationBuilder ApplyLocalDatabaseMigrations<TContext>(this IApplicationBuilder app)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(app);

        using IServiceScope scope = app.ApplicationServices.CreateScope();
        if (!ShouldApplyMigrations(scope.ServiceProvider))
        {
            return app;
        }

        ILogger logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(LocalDatabaseMigrationExtensions));

        TContext dbContext = scope.ServiceProvider.GetRequiredService<TContext>();
        TryApplyMigrationsIfNeeded(dbContext, logger);

        return app;
    }

    /// <summary>
    /// Applies pending EF Core migrations for write and read DbContexts when running locally.
    /// </summary>
    /// <typeparam name="TWriteContext">The write DbContext type.</typeparam>
    /// <typeparam name="TReadContext">The read DbContext type.</typeparam>
    /// <param name="app">The application builder.</param>
    /// <returns>The same application builder instance.</returns>
    public static IApplicationBuilder ApplyLocalDatabaseMigrations<TWriteContext, TReadContext>(this IApplicationBuilder app)
        where TWriteContext : DbContext
        where TReadContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(app);

        using IServiceScope scope = app.ApplicationServices.CreateScope();
        if (!ShouldApplyMigrations(scope.ServiceProvider))
        {
            return app;
        }

        ILogger logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(LocalDatabaseMigrationExtensions));

        TWriteContext writeContext = scope.ServiceProvider.GetRequiredService<TWriteContext>();
        TryApplyMigrationsIfNeeded(writeContext, logger);

        TReadContext readContext = scope.ServiceProvider.GetRequiredService<TReadContext>();
        if (!HasSameConnection(writeContext, readContext))
        {
            string? readConnectionString = readContext.Database.GetConnectionString();
            if (!string.IsNullOrWhiteSpace(readConnectionString))
            {
                TryApplyMigrationsIfNeeded(writeContext, logger, readConnectionString);
            }
        }

        return app;
    }

    [RequiresDynamicCode("Calls Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetMigrations()")]
    private static void ApplyMigrationsIfNeeded(DbContext dbContext, string? connectionStringOverride)
    {
        string? effectiveConnectionString = connectionStringOverride;

        if (string.IsNullOrWhiteSpace(effectiveConnectionString))
        {
            effectiveConnectionString = dbContext.Database.GetConnectionString();
        }

        if (IsPostgreSqlProvider(dbContext) && !string.IsNullOrWhiteSpace(effectiveConnectionString))
        {
            effectiveConnectionString = EnsurePostgreSqlSearchPath(effectiveConnectionString);
        }

        if (!string.IsNullOrWhiteSpace(effectiveConnectionString))
        {
            dbContext.Database.SetConnectionString(effectiveConnectionString);
        }

        if (!dbContext.Database.GetMigrations().Any())
        {
            return;
        }

        EnsurePostgreSqlSchemaContext(dbContext);

        dbContext.Database.Migrate();
    }

    private static void TryApplyMigrationsIfNeeded(DbContext dbContext, ILogger logger)
    {
        TryApplyMigrationsIfNeeded(dbContext, logger, null);
    }

    private static void TryApplyMigrationsIfNeeded(DbContext dbContext, ILogger logger, string? connectionStringOverride)
    {
        try
        {
            ApplyMigrationsIfNeeded(dbContext, connectionStringOverride);
        }
        catch (DbException exception) when (IsConnectivityFailure(exception))
        {
            logger.LogWarning(
                exception,
                "Skipping local migrations for {DbContext} because the database is unavailable.",
                dbContext.GetType().Name);
        }
    }

    private static bool IsConnectivityFailure(Exception exception)
    {
        Exception? current = exception;
        while (current is not null)
        {
            if (current is System.Net.Sockets.SocketException)
            {
                return true;
            }

            current = current.InnerException;
        }

        string message = exception.Message;
        return message.Contains("Failed to connect", StringComparison.OrdinalIgnoreCase)
            || message.Contains("actively refused", StringComparison.OrdinalIgnoreCase)
            || message.Contains("connection refused", StringComparison.OrdinalIgnoreCase)
            || message.Contains("network-related", StringComparison.OrdinalIgnoreCase)
            || message.Contains("could not open a connection", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPostgreSqlProvider(DbContext dbContext)
    {
        string? providerName = dbContext.Database.ProviderName;
        return providerName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string EnsurePostgreSqlSearchPath(string connectionString)
    {
        try
        {
            DbConnectionStringBuilder builder = new()
            {
                ConnectionString = connectionString,
            };

            if (builder.ContainsKey("Search Path") || builder.ContainsKey("SearchPath"))
            {
                return connectionString;
            }

            builder["Search Path"] = "public";
            return builder.ConnectionString;
        }
        catch (ArgumentException)
        {
            return connectionString.Contains("Search Path", StringComparison.OrdinalIgnoreCase)
                ? connectionString
                : $"{connectionString};Search Path=public";
        }
    }

    private static void EnsurePostgreSqlSchemaContext(DbContext dbContext)
    {
        if (!IsPostgreSqlProvider(dbContext))
        {
            return;
        }

        DbConnection connection = dbContext.Database.GetDbConnection();
        bool openedConnection = false;

        try
        {
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
                openedConnection = true;
            }

            using DbCommand command = connection.CreateCommand();
            command.CommandText = "CREATE SCHEMA IF NOT EXISTS public; SET search_path TO public;";
            command.ExecuteNonQuery();
        }
        finally
        {
            if (openedConnection && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    private static bool HasSameConnection(DbContext writeContext, DbContext readContext)
    {
        var writeConnection = writeContext.Database.GetConnectionString();
        var readConnection = readContext.Database.GetConnectionString();

        return string.Equals(writeConnection, readConnection, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldApplyMigrations(IServiceProvider services)
    {
        if (CodeGenerationDetector.IsRunningGeneration())
        {
            return false;
        }

        IHostEnvironment environment = services.GetRequiredService<IHostEnvironment>();
        if (environment.IsDevelopment())
        {
            return true;
        }

        IConfiguration configuration = services.GetRequiredService<IConfiguration>();
        return bool.TryParse(configuration["ASPIRE_LOCAL"], out bool aspireLocal) && aspireLocal;
    }
}

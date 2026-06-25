using System.Globalization;

namespace SharedKernel.Core.Models;

/// <summary>
/// Represents database credentials with separate admin and application users.
/// </summary>
public sealed record DatabaseCredentials
{
    /// <summary>
    /// Gets admin user credentials for database migrations and schema changes.
    /// </summary>
    public required UserCredentials Admin { get; init; }

    /// <summary>
    /// Gets application user credentials for runtime database access.
    /// </summary>
    public required UserCredentials Application { get; init; }

    /// <summary>
    /// Gets database host.
    /// </summary>
    public required string Host { get; init; }

    /// <summary>
    /// Gets database port.
    /// </summary>
    public required int Port { get; init; }

    /// <summary>
    /// Gets database name.
    /// </summary>
    public required string Database { get; init; }

    /// <summary>
    /// Gets additional connection parameters.
    /// </summary>
    public IReadOnlyDictionary<string, string>? AdditionalParameters { get; init; }

    /// <summary>
    /// Gets the connection string for admin user.
    /// </summary>
    /// <returns></returns>
    public string GetAdminConnectionString() =>
        BuildConnectionString(Admin, null, null);

    /// <summary>
    /// Gets the connection string for admin user with optional host/port override.
    /// </summary>
    /// <returns></returns>
    public string GetAdminConnectionString(string? overrideHost, int? overridePort) =>
        BuildConnectionString(Admin, overrideHost, overridePort);

    /// <summary>
    /// Gets the connection string for application user.
    /// </summary>
    /// <returns></returns>
    public string GetApplicationConnectionString() =>
        BuildConnectionString(Application, null, null);

    /// <summary>
    /// Gets the connection string for application user with optional host/port override.
    /// Useful for read replicas that use different host/port but same credentials.
    /// </summary>
    /// <returns></returns>
    public string GetApplicationConnectionString(string? overrideHost, int? overridePort) =>
        BuildConnectionString(Application, overrideHost, overridePort);

    private string BuildConnectionString(UserCredentials credentials, string? overrideHost, int? overridePort)
    {
        var host = overrideHost ?? Host;
        var port = overridePort ?? Port;

        var builder = BuildPostgreSqlConnectionString(credentials, host, port);

        if (AdditionalParameters is not null)
        {
            foreach (var (key, value) in AdditionalParameters)
            {
                builder.Append(CultureInfo.InvariantCulture, $"{key}={value};");
            }
        }

        return builder.ToString();
    }

    private System.Text.StringBuilder BuildPostgreSqlConnectionString(UserCredentials credentials, string host, int port)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"Host={host};");
        builder.Append(CultureInfo.InvariantCulture, $"Port={port};");
        builder.Append(CultureInfo.InvariantCulture, $"Database={Database};");
        builder.Append(CultureInfo.InvariantCulture, $"Username={credentials.Username};");
        builder.Append(CultureInfo.InvariantCulture, $"Password={credentials.Password};");
        return builder;
    }
}

/// <summary>
/// User credentials for database access.
/// </summary>
public sealed record UserCredentials
{
    /// <summary>
    /// Gets username.
    /// </summary>
    public required string Username { get; init; }

    /// <summary>
    /// Gets password.
    /// </summary>
    public required string Password { get; init; }
}

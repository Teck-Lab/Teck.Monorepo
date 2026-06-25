namespace SharedKernel.Core.Models;

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

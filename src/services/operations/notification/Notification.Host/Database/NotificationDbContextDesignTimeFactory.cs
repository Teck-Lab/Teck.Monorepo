using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Notifications.Application.Database;

namespace Notifications.Host.Database;

/// <summary>Creates the write context for EF migration tooling without starting the host.</summary>
public sealed class NotificationDbContextDesignTimeFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    /// <inheritdoc />
    public NotificationDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("NOTIFICATION_DESIGN_TIME_CONNECTION")
            ?? throw new InvalidOperationException("NOTIFICATION_DESIGN_TIME_CONNECTION is required for design-time EF operations.");
        var options = new DbContextOptionsBuilder<NotificationDbContext>().UseNpgsql(connection, npgsql => npgsql.MigrationsAssembly(typeof(NotificationDbContextDesignTimeFactory).Assembly.FullName)).Options;
        return new NotificationDbContext(options, null!);
    }
}

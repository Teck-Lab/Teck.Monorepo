using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SharedKernel.Infrastructure.Database.EFCore.Config;

/// <summary>
/// Provides shared configuration methods for entity configurations across all services.
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Applies standard audit property configurations to an entity.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    public static void ConfigureAuditProperties<T>(this EntityTypeBuilder<T> builder)
    where T : class
    {
        // Audit properties
        builder.Property("CreatedAt")
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        builder.Property("CreatedBy")
            .HasMaxLength(100);

        builder.Property("UpdatedOn")
            .HasColumnType("timestamp with time zone");

        builder.Property("UpdatedBy")
            .HasMaxLength(100);

        builder.Property("DeletedOn")
            .HasColumnType("timestamp with time zone");

        builder.Property("DeletedBy")
            .HasMaxLength(100);

        builder.Property("IsDeleted")
            .HasDefaultValue(false)
            .IsRequired();
    }
}

// <copyright file="TenantDbContextOptionsExtension.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using JasperFx.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Infrastructure.Database.EFCore
{
    /// <summary>
    /// EF Core options extension that carries the resolved tenant identifier
    /// alongside <see cref="DbContextOptions"/>.
    /// </summary>
    /// <remarks>
    /// Wolverine's <c>AddDbContextWithWolverineManagedMultiTenancy</c> constructs
    /// tenant-scoped <see cref="DbContext"/> instances via
    /// <c>System.Activator.CreateInstance</c> with a single
    /// <see cref="DbContextOptions"/> argument. That activation path bypasses DI and therefore
    /// cannot inject Finbuckle's <c>IMultiTenantContextAccessor</c>. This extension lets the
    /// Wolverine configuration lambda stamp the tenant id onto the options so
    /// <see cref="BaseDbContext"/> can resolve it without a scoped accessor.
    /// </remarks>
    public sealed class TenantDbContextOptionsExtension : IDbContextOptionsExtension
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TenantDbContextOptionsExtension"/> class.
        /// </summary>
        /// <param name="tenantId">The resolved tenant identifier.</param>
        public TenantDbContextOptionsExtension(string tenantId)
        {
            TenantId = tenantId;
        }

        /// <summary>
        /// Gets the tenant identifier carried by this extension.
        /// </summary>
        public string TenantId { get; }

        /// <inheritdoc/>
        public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);

        /// <inheritdoc/>
        public void ApplyServices(IServiceCollection services)
        {
        }

        /// <inheritdoc/>
        public void Validate(IDbContextOptions options)
        {
        }

        private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
        {
            public ExtensionInfo(TenantDbContextOptionsExtension extension)
                : base(extension)
            {
            }

            public override bool IsDatabaseProvider => false;

            public override string LogFragment => $"TenantId={((TenantDbContextOptionsExtension)Extension).TenantId} ";

            public override int GetServiceProviderHashCode() =>
                ((TenantDbContextOptionsExtension)Extension).TenantId.GetHashCode(StringComparison.Ordinal);

            public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
                other is ExtensionInfo otherInfo &&
                string.Equals(
                    ((TenantDbContextOptionsExtension)Extension).TenantId,
                    ((TenantDbContextOptionsExtension)otherInfo.Extension).TenantId,
                    StringComparison.Ordinal);

            public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            {
                ArgumentNullException.ThrowIfNull(debugInfo);
                debugInfo["Tenant:Id"] = ((TenantDbContextOptionsExtension)Extension).TenantId;
            }
        }
    }

}

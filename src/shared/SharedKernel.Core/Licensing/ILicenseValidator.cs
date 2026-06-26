// <copyright file="ILicenseValidator.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

namespace SharedKernel.Core.Licensing;

/// <summary>
/// Validates license status and quotas for a tenant or location.
/// </summary>
public interface ILicenseValidator
{
    /// <summary>
    /// Validates the license for the specified tenant and optional location.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="locationId">The location identifier, or null for tenant-level validation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The license validation result.</returns>
    Task<LicenseValidationResult> ValidateAsync(
        string tenantId,
        string? locationId,
        CancellationToken cancellationToken);
}

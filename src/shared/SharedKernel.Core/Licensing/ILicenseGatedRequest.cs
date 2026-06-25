// <copyright file="ILicenseGatedRequest.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

namespace SharedKernel.Core.Licensing;

/// <summary>
/// Marker interface for requests that require license validation.
/// </summary>
public interface ILicenseGatedRequest
{
    /// <summary>
    /// Gets the tenant identifier for license validation.
    /// </summary>
    string TenantId { get; }

    /// <summary>
    /// Gets the location identifier for license validation, or null for tenant-level.
    /// </summary>
    string? LocationId { get; }
}

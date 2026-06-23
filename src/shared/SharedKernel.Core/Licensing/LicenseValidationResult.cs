// <copyright file="LicenseValidationResult.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

namespace SharedKernel.Core.Licensing;

/// <summary>
/// Represents the result of a license validation check.
/// </summary>
public sealed class LicenseValidationResult
{
    /// <summary>
    /// Gets a value indicating whether the license is valid.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Gets the error message if the license is invalid.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the maximum number of access points allowed.
    /// </summary>
    public int? MaxAccessPoints { get; init; }

    /// <summary>
    /// Gets the maximum number of devices allowed.
    /// </summary>
    public int? MaxDevices { get; init; }

    /// <summary>
    /// Gets the maximum number of products allowed.
    /// </summary>
    public int? MaxProducts { get; init; }

    /// <summary>
    /// Gets the maximum number of locations allowed.
    /// </summary>
    public int? MaxLocations { get; init; }

    /// <summary>
    /// Gets a value indicating whether custom branding is supported.
    /// </summary>
    public bool SupportsCustomBranding { get; init; }

    /// <summary>
    /// Gets a value indicating whether analytics is supported.
    /// </summary>
    public bool SupportsAnalytics { get; init; }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static LicenseValidationResult Success(
        int? maxAccessPoints,
        int? maxDevices,
        int? maxProducts,
        int? maxLocations,
        bool supportsCustomBranding,
        bool supportsAnalytics)
    {
        return new LicenseValidationResult
        {
            IsValid = true,
            MaxAccessPoints = maxAccessPoints,
            MaxDevices = maxDevices,
            MaxProducts = maxProducts,
            MaxLocations = maxLocations,
            SupportsCustomBranding = supportsCustomBranding,
            SupportsAnalytics = supportsAnalytics,
        };
    }

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    /// <param name="errorMessage">The error message.</param>
    public static LicenseValidationResult Failure(string errorMessage)
    {
        return new LicenseValidationResult
        {
            IsValid = false,
            ErrorMessage = errorMessage,
        };
    }
}

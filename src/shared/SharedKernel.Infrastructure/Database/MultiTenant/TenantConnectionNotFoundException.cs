// <copyright file="TenantConnectionNotFoundException.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Diagnostics.CodeAnalysis;

namespace SharedKernel.Infrastructure.Database.MultiTenant;

/// <summary>
/// Thrown when a tenant's connection string cannot be found in OpenBao/Vault.
/// </summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Intentionally requires a message; parameterless construction is not meaningful.")]
public sealed class TenantConnectionNotFoundException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TenantConnectionNotFoundException"/> class.
    /// </summary>
    /// <param name="message">Exception message.</param>
    public TenantConnectionNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantConnectionNotFoundException"/> class.
    /// </summary>
    /// <param name="message">Exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public TenantConnectionNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

// <copyright file="EslProvider.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using Ardalis.SmartEnum;

namespace SharedKernel.Core.Devices;

/// <summary>
/// Identifies the ESL vendor integration driver used to communicate with a device.
/// Stored as a name string in the database.
/// </summary>
public sealed class EslProvider : SmartEnum<EslProvider>
{
    /// <summary>
    /// No provider assigned or provider is unknown.
    /// </summary>
    public static readonly EslProvider Unknown = new(nameof(Unknown), 0);

    /// <summary>
    /// Hanshow Technology ESL platform.
    /// </summary>
    public static readonly EslProvider Hanshow = new(nameof(Hanshow), 1);

    /// <summary>
    /// SoluM ESL platform.
    /// </summary>
    public static readonly EslProvider SoluM = new(nameof(SoluM), 2);

    private EslProvider(string name, int value)
        : base(name, value)
    {
    }
}

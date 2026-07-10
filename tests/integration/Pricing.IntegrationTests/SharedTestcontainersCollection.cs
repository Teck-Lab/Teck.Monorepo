// <copyright file="SharedTestcontainersCollection.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Diagnostics.CodeAnalysis;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Pricing.IntegrationTests;

/// <summary>
/// Re-declares the shared xUnit collection so that the test runner discovers
/// <see cref="SharedTestcontainersFixture"/> when resolving constructor parameters.
/// xUnit v3 only searches the executing test assembly for <c>[CollectionDefinition]</c> types;
/// the definition in <c>Teck.Platform.IntegrationTests.Shared</c> is not auto-discovered.
/// </summary>
[CollectionDefinition("SharedTestcontainers")]
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "xUnit requires collection definition classes to be public")]
public class SharedTestcontainersCollection : ICollectionFixture<SharedTestcontainersFixture>
{
}

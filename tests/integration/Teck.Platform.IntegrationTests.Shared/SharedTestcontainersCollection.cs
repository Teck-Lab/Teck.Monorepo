// <copyright file="SharedTestcontainersCollection.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace Teck.Platform.IntegrationTests.Shared;

/// <summary>
/// xUnit collection definition that shares one <see cref="SharedTestcontainersFixture"/>
/// across all test classes in the collection.
/// </summary>
[CollectionDefinition("SharedTestcontainers")]
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "xUnit requires collection definition classes to be public")]
public class SharedTestcontainersCollection : ICollectionFixture<SharedTestcontainersFixture>
{
}

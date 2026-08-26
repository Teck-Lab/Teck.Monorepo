using System.Diagnostics.CodeAnalysis;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Aspire.AppHost.IntegrationTests;

/// <summary>Registers the shared PostgreSQL/RabbitMQ fixture for this executing test assembly.</summary>
[CollectionDefinition("SharedTestcontainers")]
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "xUnit requires collection definition classes to be public")]
public sealed class SharedTestcontainersCollection : ICollectionFixture<SharedTestcontainersFixture>
{
}

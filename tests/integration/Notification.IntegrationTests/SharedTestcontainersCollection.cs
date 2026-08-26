using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Notifications.IntegrationTests;

[CollectionDefinition("SharedTestcontainers")]
public sealed class SharedTestcontainersCollection : ICollectionFixture<SharedTestcontainersFixture>
{
}

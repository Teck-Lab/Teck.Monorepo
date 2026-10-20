using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Baskets.IntegrationTests;

/// <summary>Shares real Keycloak, PostgreSQL, and RabbitMQ infrastructure for direct auth tests.</summary>
[CollectionDefinition("KeycloakTestRealm")]
public sealed class KeycloakTestRealmCollection : ICollectionFixture<KeycloakTestRealmFixture>, ICollectionFixture<SharedTestcontainersFixture>;

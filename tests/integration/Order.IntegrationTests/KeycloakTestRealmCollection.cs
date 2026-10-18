using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Orders.IntegrationTests;

/// <summary>Shares real Keycloak, PostgreSQL, and RabbitMQ containers across direct Order authorization tests.</summary>
[CollectionDefinition("KeycloakTestRealm")]
public sealed class KeycloakTestRealmCollection : ICollectionFixture<KeycloakTestRealmFixture>, ICollectionFixture<SharedTestcontainersFixture>;

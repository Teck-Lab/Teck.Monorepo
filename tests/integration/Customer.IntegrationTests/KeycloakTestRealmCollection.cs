using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Customers.IntegrationTests;

/// <summary>Shares real Keycloak, PostgreSQL, and RabbitMQ across direct Customer authorization tests.</summary>
[CollectionDefinition("KeycloakTestRealm")]
public sealed class KeycloakTestRealmCollection : ICollectionFixture<KeycloakTestRealmFixture>, ICollectionFixture<SharedTestcontainersFixture>;

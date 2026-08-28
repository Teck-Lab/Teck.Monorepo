using Xunit;

// This assembly owns three container-heavy collections: "AppHost" boots the whole distributed
// application (Postgres, Keycloak, RabbitMQ, Redis and every service), "LocalIdentityKeycloak"
// starts Keycloak plus Postgres and the routed service hosts, and "SharedTestcontainers" starts
// its own Postgres and RabbitMQ.
//
// DisableParallelization on a CollectionDefinition only serializes tests *inside* that
// collection; xUnit still runs separate collections concurrently. On a two-core CI runner that
// meant several DCP/Docker stacks competing for the same cores, memory and image pulls, which
// pushed a three-minute project past thirty minutes without finishing. Serialize the whole
// assembly so each stack gets the runner to itself.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

# Identity provisioning handover

This is the staging and production identity contract. It is derived from the
committed local reference manifests:

- `src/aspire/Teck.AppHost/realms/teck-realm.json` (production-safe object template; no user passwords or enabled direct grants)
- `src/aspire/Teck.AppHost/test-realms/teck-realm.json` (isolated local test import; test-only users and password-grant clients)
- `src/aspire/Teck.AppHost/identity/local-organizations.json`

The default AppHost imports only `realms/`; never place test realm documents in that import directory. LocalIdentity/AppHost integration tests explicitly select `test-realms/`.

Teck.Terraform and Teck.GitOps must provision the environment-specific
equivalent of every object below. Both committed manifests are templates only,
not deployable configuration or sources of values to promote. In particular,
**no credential value committed in this repository may be promoted to staging
or production**. Production deployment requires environment-specific SSL,
redirect URLs/web origins, secrets and identities; do not deploy committed local values.

The test-only realm has one password-grant client, confidential `integration-test-client`, for automated integration fixtures. The staff dashboard uses a separate confidential standard-flow client with S256 PKCE.

### Local browser dashboard client

The test-only `teck-dashboard` client accepts `http://localhost:3001/api/auth/callback/keycloak`. Its access token must contain a `web-admin-gateway` audience and its ID token must map realm roles to `realm_access.roles`, so Auth.js can display read-only versus manager controls. The distinct confidential `web-customer-gateway` exchange client has only its own and `catalog-api` audiences; `web-admin-gateway` has its own, `catalog-api`, and `pricing-api` audiences. Both use standard token exchange; only the admin gateway exposes the operator Catalog/Pricing paths, and its JSON and Scalar contracts require an internal role. Keep every client secret server-side; never promote committed test credentials to deployed environments.

Every endpoint permission in the shared commerce/operations services has a confidential authorization-services resource client: `basket-api` (`basket`: `checkout`, `merge`), `catalog-api` (`catalog`: `read`, `manage`, `supplier-read`), `customer-api` (`customer`: `read`, `manage`), `inventory-api` (`inventory`: `read`, `adjust`, `register`, `set-location-priorities`, `set-policy`), and `billing-api` (`billing`: `read`). Each resource client emits its own audience and the Keycloak organization-membership claim containing organization identifiers. `platform-reader` and `platform-manager` may read; only `platform-manager` may use non-read scopes, including supplier cost history. Order and Pricing retain their existing resource policies. The integration test client requests optional audiences for the services under test and the gateway.
The production and test realms both map `order-api` and `pricing-api` into their access-token audiences. Direct Order and Pricing integration tests import the isolated realm, request `openid organization:*`, provision real Keycloak organization membership through its Admin API, and exercise actual service endpoints backed by PostgreSQL and RabbitMQ. Pricing verifies anonymous 401, reader 403, manager-created price-list HTTP read-back and tenant-scoped database persistence; Order verifies anonymous 401, reader HTTP read-back of an ActionRequired order, manager-authorized payment retry 202 with retry state persisted in PostgreSQL, reader-forbidden payment retry, and tenant-scoped persistence. Routed AppHost tests additionally exercise read and management authorization through the gateway to real in-process Order and Pricing hosts. No mock authentication handler or caller-controlled tenant header is used in those authorization paths.

## Staff dashboard local OAuth client

The test realm additionally provisions confidential `teck-dashboard` with standard flow, S256 PKCE, no direct grants, and the exact callback `http://localhost:3001/api/auth/callback/keycloak`. Its `local-only-test-dashboard-secret` is for the local test realm only. Start the dashboard on port 3001 with server-only `AUTH_SECRET`, `AUTH_URL`, `KEYCLOAK_ISSUER`, `KEYCLOAK_CLIENT_ID`, `KEYCLOAK_CLIENT_SECRET`, and `TECK_GATEWAY_URL` variables. Never commit or expose production credentials; deployment must use unique secret values and exact HTTPS redirect origins.
Automated direct-service authorization tests use real Keycloak user access tokens through the test-only password grant, including signed organization-membership claims with Keycloak-generated organization IDs. The grant is enabled only for `integration-test-client` in the isolated test realm; production and default local AppHost realm imports keep password grants disabled.

`Basket.Host` keeps current/item routes anonymous. Each storefront deployment must
provide its organization ID as `Basket__StorefrontTenantId` from trusted server
configuration; the default is unset and does not assign an anonymous tenant.
The guest `X-Basket-Token` identifies a basket within that tenant, never the
tenant itself. Authenticated requests continue to resolve the tenant from signed
organization claims. Guest creation and subsequent item requests must use the
same configured storefront tenant; merge requires membership in that tenant.

## Environment-owned values and secrets

The following values must be supplied independently for staging and production:

| Value | Environment rule |
| --- | --- |
| Issuer and Keycloak authorization-server URL | Use that environment's public identity endpoint; do not reuse the local host. |
| `teck-dashboard` redirect URIs and web origins | Use that environment's dashboard URL(s); do not reuse local redirect URIs or origins. |
| Organization aliases, domains, and generated organization IDs | Choose the environment's tenant naming, then capture the identity provider's generated ID for each organization. Do not reuse local aliases, domains, or IDs. |
| Tenant-registry rows | Create one row per environment organization using its generated organization ID and that environment's tenant configuration. |
| `Basket__StorefrontTenantId` | Set to the storefront deployment's Keycloak-generated organization ID from trusted server configuration, not a request header. |
| Every confidential-client secret | **SECRET.** Generate and store a distinct secret for each environment and client; never copy a committed local value. |
| Person password credentials and Keycloak administration credentials | **SECRET.** Create and manage them through the environment secret store; do not reuse local passwords or administrator credentials. |

The required confidential-client secrets are for `order-api`, `pricing-api`,
`teck-dashboard`, `web-customer-gateway`, `web-admin-gateway`, `basket-api`, `catalog-api`, `customer-api`,
`inventory-api`, and `billing-api`. Resource clients do not enable password grants.

## Identity-provider inventory

Provision these identity-provider objects before writing any tenant-registry
record. Object names, permissions, claims, and flags below describe the
platform contract; environment-owned values are called out above.

### Realm and roles

Create an enabled `teck` realm with `sslRequired` set to `external` in the
production-safe template and the organizations capability enabled
(`organizationsEnabled: true`). Review and set the appropriate transport-security
policy for each deployed environment. Create these realm roles:

- `platform-reader`
- `platform-manager`

### Clients

All listed clients use OpenID Connect and are enabled.

| Client | Client flags | Secret handling |
| --- | --- | --- |
| `order-api` | Confidential (`publicClient: false`), `clientAuthenticatorType: client-secret`, authorization services enabled. | **SECRET** — generate a per-environment client secret. |
| `catalog-api` | Confidential (`publicClient: false`), `clientAuthenticatorType: client-secret`, authorization services enabled, direct access grants disabled. | **SECRET** — generate a per-environment client secret. |
| `basket-api`, `customer-api`, `inventory-api`, `billing-api` | Confidential (`publicClient: false`), `clientAuthenticatorType: client-secret`, authorization services enabled, direct access grants disabled. | **SECRET** — generate distinct per-environment client secrets. |
| `pricing-api` | Confidential (`publicClient: false`), `clientAuthenticatorType: client-secret`, authorization services enabled. | **SECRET** — generate a per-environment client secret. |
| `teck-dashboard` | Confidential (`publicClient: false`), `clientAuthenticatorType: client-secret`, standard flow enabled, direct access grants disabled, and `pkce.code.challenge.method: S256`. Configure environment-specific redirect URIs and web origins. | **SECRET** — generate a per-environment client secret. |
| `web-customer-gateway`, `web-admin-gateway` | Distinct confidential exchange clients (`publicClient: false`), `clientAuthenticatorType: client-secret`, direct access grants disabled, and `standard.token.exchange.enabled: true`. | **SECRET** — generate distinct per-environment client secrets. |

### Protocol mappers

Create the following mappers and claim behavior:

- Every resource client (`order-api`, `pricing-api`, `basket-api`,
  `catalog-api`, `customer-api`, `inventory-api`, `billing-api`) and
  `teck-dashboard`: an `organization-membership`
  `oidc-organization-membership-mapper` that emits the JSON `organization`
  claim with organization IDs in access tokens, ID tokens, user-info responses,
  and token introspection. Each resource client also maps its own API audience
  into access tokens.
- Both web gateway clients: the same `organization-membership` mapper, plus
  a `tenant_id` mapper from the user attribute into the access token, and
  their own gateway audience mapper. `web-customer-gateway` additionally maps
  `catalog-api`; `web-admin-gateway` additionally maps `catalog-api` and
  `pricing-api`. The dashboard maps only `web-admin-gateway` as its gateway
  audience. No gateway maps `order-api` for this web cutover.

The isolated `integration-test-client` requests the appropriate optional API
audience scopes; resource clients never accept password grants.

### Authorization resources, scopes, and permissions

Enable enforcing authorization services for each resource client listed above.
Provision the resources, scopes, and positive/unanimous policies exactly as
follows:

| Client | Resource and scopes | Permission definitions |
| --- | --- | --- |
| `order-api` | Resource `order`; scopes `read` and `retry-payment`. | `order-read-roles` is a role policy for `platform-reader` or `platform-manager`; `order-read` is a scope policy for `read` that applies `order-read-roles`. `order-retry-payment-manager` is a role policy for `platform-manager`; `order-retry-payment` is a scope policy for `retry-payment` that applies `order-retry-payment-manager`. |
| `pricing-api` | Resource `pricing`; scopes `read` and `manage`. | `pricing-read-roles` is a role policy for `platform-reader` or `platform-manager`; `pricing-read` is a scope policy for `read` that applies `pricing-read-roles`. `pricing-manage-manager` is a role policy for `platform-manager`; `pricing-manage` is a scope policy for `manage` that applies `pricing-manage-manager`. |
| `catalog-api` | Resource `catalog`; scopes `read`, `manage`, `supplier-read`. | Reader and manager may `read`; only manager may `manage` or `supplier-read` (supplier cost history). |
| `customer-api` | Resource `customer`; scopes `read`, `manage`. | Reader and manager may `read`; only manager may `manage`. |
| `basket-api` | Resource `basket`; scopes `checkout`, `merge`. | Only manager may `checkout` or `merge`; current/item routes remain anonymous and subject/guest-token owned. |
| `inventory-api` | Resource `inventory`; scopes `read`, `adjust`, `register`, `set-location-priorities`, `set-policy`. | Reader and manager may `read`; only manager may mutate inventory. |
| `billing-api` | Resource `billing`; scope `read`. | Reader and manager may `read`. |

Every listed policy uses `logic: POSITIVE` and
`decisionStrategy: UNANIMOUS`; role policies retain `fetchRoles: true`.

### People, organizations, and memberships

Create enabled people before assigning organization membership. The local
reference inventory has these two enabled identities and realm-role grants;
their password credentials are **SECRETS** and must be independently created
per environment:

| Local reference person | Realm roles | Required organization memberships |
| --- | --- | --- |
| `dev@teck.local` | `platform-reader`, `platform-manager` | Both local reference organizations. |
| `dev-reader@teck.local` | `platform-reader` | The local Alpha reference organization only. |

The local reference organizations are an inventory only:

| Local reference organization | Alias | Local domain | Enabled | Members | Local tenant settings |
| --- | --- | --- | --- | --- | --- |
| `Teck Local Alpha` | `teck-local-alpha` | `alpha.teck.local` (unverified) | Yes | `dev@teck.local`, `dev-reader@teck.local`, `integration-manager`, `integration-reader` (test realm only) | identifier `teck-local-alpha`; shared Postgres; no read replicas. |
| `Teck Local Beta` | `teck-local-beta` | `beta.teck.local` (unverified) | Yes | `dev@teck.local` | identifier `teck-local-beta`; shared Postgres; no read replicas. |

Staging and production must each create the organizations, domains, people, and
memberships appropriate to that environment; aliases and domains must not be
copied from local. Read back and retain the generated identity-provider ID for
every organization, because it is the primary key of the corresponding
tenant-registry record.

## Tenant registry and required order

For every organization, write one Customer tenant-registry record containing:

- the generated organization ID as the tenant ID;
- the environment-specific tenant identifier;
- database strategy;
- database provider; and
- whether the tenant has read replicas.

Provision in this order:

1. Create or reconcile the realm, roles, people, clients, mappers,
   authorization resources/scopes/policies, organizations, and memberships.
2. Read back each generated organization ID, verify the complete organization
   set, then create or reconcile its tenant-registry record.

The registry is the platform's tenant authority. An organization that exists
in the identity provider but has no matching registry row has no recognized
tenant ID or database configuration, so every request for that tenant fails.

## Local command and ownership boundary

For local development, run `aspire run` from `src/aspire/Teck.AppHost`; it
starts the `local-identity` reconciler after Keycloak and Customer are
available. That local command is not a staging or production deployment
procedure. Teck.Terraform and Teck.GitOps own deployed identity provisioning,
secret injection, and tenant-registry reconciliation using environment-specific
inputs.

The developer command keeps Keycloak at `http://localhost:8080`, matching the
committed Development settings and browser redirect addresses. AppHost
integration tests explicitly disable that fixed host binding so each test run
uses an isolated dynamic port; this test-only override does not change the
local development address.

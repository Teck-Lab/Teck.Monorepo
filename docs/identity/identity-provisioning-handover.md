# Identity provisioning handover

This is the staging and production identity contract. It is derived from the
committed local reference manifests:

- `src/aspire/Teck.AppHost/realms/teck-realm.json`
- `src/aspire/Teck.AppHost/realms/local-organizations.json`

Teck.Terraform and Teck.GitOps must provision the environment-specific
equivalent of every object below. The local manifests are an object inventory,
not a source of values to promote. In particular, **no credential value
committed in this repository may be promoted to staging or production**.

## Environment-owned values and secrets

The following values must be supplied independently for staging and production:

| Value | Environment rule |
| --- | --- |
| Issuer and Keycloak authorization-server URL | Use that environment's public identity endpoint; do not reuse the local host. |
| `teck-dashboard` redirect URIs and web origins | Use that environment's dashboard URL(s); do not reuse local redirect URIs or origins. |
| Organization aliases, domains, and generated organization IDs | Choose the environment's tenant naming, then capture the identity provider's generated ID for each organization. Do not reuse local aliases, domains, or IDs. |
| Tenant-registry rows | Create one row per environment organization using its generated organization ID and that environment's tenant configuration. |
| Every confidential-client secret | **SECRET.** Generate and store a distinct secret for each environment and client; never copy a committed local value. |
| Person password credentials and Keycloak administration credentials | **SECRET.** Create and manage them through the environment secret store; do not reuse local passwords or administrator credentials. |

The required confidential-client secrets are for `order-api`, `pricing-api`,
`teck-dashboard`, and `public-gateway`. The `catalog-api` client is public and
does not have a client secret in the local reference inventory.

## Identity-provider inventory

Provision these identity-provider objects before writing any tenant-registry
record. Object names, permissions, claims, and flags below describe the
platform contract; environment-owned values are called out above.

### Realm and roles

Create an enabled `teck` realm with `sslRequired` set to `none` in the local
reference and the organizations capability enabled (`organizationsEnabled:
true`). Configure the appropriate transport-security setting for each deployed
environment; the local `none` setting is not a production value. Create these
realm roles:

- `platform-reader`
- `platform-manager`

### Clients

All five clients use OpenID Connect and are enabled.

| Client | Client flags | Secret handling |
| --- | --- | --- |
| `order-api` | Confidential (`publicClient: false`), `clientAuthenticatorType: client-secret`, authorization services enabled. | **SECRET** — generate a per-environment client secret. |
| `catalog-api` | Public (`publicClient: true`); no client authenticator or authorization service. | No client secret. |
| `pricing-api` | Confidential (`publicClient: false`), `clientAuthenticatorType: client-secret`, authorization services enabled. | **SECRET** — generate a per-environment client secret. |
| `teck-dashboard` | Confidential (`publicClient: false`), `clientAuthenticatorType: client-secret`, standard flow enabled, direct access grants disabled, and `pkce.code.challenge.method: S256`. Configure environment-specific redirect URIs and web origins. | **SECRET** — generate a per-environment client secret. |
| `public-gateway` | Confidential (`publicClient: false`), `clientAuthenticatorType: client-secret`, direct access grants enabled, and `standard.token.exchange.enabled: true`. | **SECRET** — generate a per-environment client secret. |

### Protocol mappers

Create the following mappers and claim behavior:

- `order-api`, `pricing-api`, and `teck-dashboard`: an
  `organization-membership` `oidc-organization-membership-mapper` that emits
  the multivalued JSON `organization` claim, includes the organization ID, and
  adds it to access tokens, ID tokens, user-info responses, and token
  introspection.
- `public-gateway`: the same `organization-membership` mapper and four
  additional mappers:
  - `tenant_id`: an `oidc-usermodel-attribute-mapper` from the `tenant_id`
    user attribute to the single string `tenant_id` access-token claim only.
  - `public-gateway-audience`: an `oidc-audience-mapper` that adds
    `public-gateway` to the access-token audience only.
  - `order-api-audience`: an `oidc-audience-mapper` that adds `order-api` to
    the access-token audience only.
  - `pricing-api-audience`: an `oidc-audience-mapper` that adds `pricing-api`
    to the access-token audience only.

`catalog-api` has no protocol mappers in the committed local manifest.

### Authorization resources, scopes, and permissions

Enable enforcing authorization services for `order-api` and `pricing-api`.
Provision the resources, scopes, and positive/unanimous policies exactly as
follows:

| Client | Resource and scopes | Permission definitions |
| --- | --- | --- |
| `order-api` | Resource `order`; scopes `read` and `retry-payment`. | `order-read-roles` is a role policy for `platform-reader` or `platform-manager`; `order-read` is a scope policy for `read` that applies `order-read-roles`. `order-retry-payment-manager` is a role policy for `platform-manager`; `order-retry-payment` is a scope policy for `retry-payment` that applies `order-retry-payment-manager`. |
| `pricing-api` | Resource `pricing`; scopes `read` and `manage`. | `pricing-read-roles` is a role policy for `platform-reader` or `platform-manager`; `pricing-read` is a scope policy for `read` that applies `pricing-read-roles`. `pricing-manage-manager` is a role policy for `platform-manager`; `pricing-manage` is a scope policy for `manage` that applies `pricing-manage-manager`. |

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
| `Teck Local Alpha` | `teck-local-alpha` | `alpha.teck.local` (unverified) | Yes | `dev@teck.local`, `dev-reader@teck.local` | identifier `teck-local-alpha`; shared Postgres; no read replicas. |
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

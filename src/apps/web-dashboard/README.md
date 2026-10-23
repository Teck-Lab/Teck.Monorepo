# Staff price-list dashboard

The dashboard uses a confidential Keycloak client with Authorization Code + PKCE (S256). Auth.js stores OAuth tokens in an encrypted, HTTP-only server session cookie; browser components never receive bearer tokens or the Keycloak client secret. Access tokens are refreshed server-side and data calls use only `TECK_GATEWAY_URL`.

Required server environment:

- `AUTH_SECRET`: random deployment secret, at least 32 bytes.
- `AUTH_URL`: public dashboard origin used for Keycloak callback URLs.
- `KEYCLOAK_ISSUER`: realm issuer URL, e.g. `https://sso.example/realms/teck`.
- `KEYCLOAK_CLIENT_ID`: `teck-dashboard`.
- `KEYCLOAK_CLIENT_SECRET`: confidential client secret; never prefix with `NEXT_PUBLIC_`.
- `TECK_GATEWAY_URL`: gateway origin (for example `http://localhost:5100`).

The Keycloak `teck-dashboard` client must allow the exact `${AUTH_URL}/api/auth/callback/keycloak` redirect URI and use S256 PKCE. The dashboard reads products from `/api/v1/catalog/products` and price lists from `/api/v1/admin/pricing/price-lists`; persisted price edits use `/api/v1/admin/pricing/price-lists/{id}/prices/{productId}`. Requests stay server-side and carry the session user's access token. The gateway exchanges that token for the downstream audience; service authorization enforces permission and organization scope. Shared Catalog reads accept signed-in readers; operator Pricing routes require `platform-manager`. Internal gateway JSON and Scalar accept `platform-manager` or an explicitly provisioned `platform-developer` role.

The typed `@teck/api-client` contract is generated from the curated admin gateway OpenAPI contract at `src/services/gateway/shared/Contracts/gateway-admin-v1.openapi.json`, not merged from service-host documents. Customer read routes remain represented in that document. Run `bunx nx run api-client:sync-openapi` and `bunx nx run api-client:generate` after an approved gateway contract change. `bunx nx run api-client:check-openapi` compares the package snapshot with that checked gateway contract, and `bunx nx run api-client:check-generated` verifies generated TypeScript.

# Staff price-list dashboard

The dashboard uses a confidential Keycloak client with Authorization Code + PKCE (S256). Auth.js stores OAuth tokens in an encrypted, HTTP-only server session cookie; browser components never receive bearer tokens or the Keycloak client secret. Access tokens are refreshed server-side and data calls use only `TECK_GATEWAY_URL`.

Required server environment:

- `AUTH_SECRET`: random deployment secret, at least 32 bytes.
- `AUTH_URL`: public dashboard origin used for Keycloak callback URLs.
- `KEYCLOAK_ISSUER`: realm issuer URL, e.g. `https://sso.example/realms/teck`.
- `KEYCLOAK_CLIENT_ID`: `teck-dashboard`.
- `KEYCLOAK_CLIENT_SECRET`: confidential client secret; never prefix with `NEXT_PUBLIC_`.
- `TECK_GATEWAY_URL`: gateway origin (for example `http://localhost:5100`); YARP routes `/products` and `/price-lists` at the root.

The Keycloak `teck-dashboard` client must allow the exact `${AUTH_URL}/api/auth/callback/keycloak` redirect URI and use S256 PKCE. The gateway audience-exchanges the user's signed token; services enforce permission and organization scope.

The typed `@teck/api-client` contract is generated from the actual Catalog and Pricing hosts, not authored separately. Start both hosts with OpenAPI enabled, then run `bunx nx run api-client:sync-openapi` and `bunx nx run api-client:generate` from the repository root. The defaults read `http://127.0.0.1:5101/openapi/v1/openapi.json` and port 5102 respectively; set `CATALOG_OPENAPI_URL` and `PRICING_OPENAPI_URL` for other hosts. `bunx nx run api-client:check-openapi` compares live service documents with the checked snapshot, and `bunx nx run api-client:check-generated` verifies generated TypeScript. The gateway serves the same paths at its root.

# Staff dashboard

This separate Next.js app is for staff catalog operations. It uses Keycloak OIDC
through NextAuth and the generated `@teck/api-client` from server-side route
handlers. Browser requests carry the encrypted NextAuth session cookie; the
server forwards the user's Keycloak access token to YARP, which exchanges it for
Catalog and enforces Keycloak permissions and signed tenant membership. No
Catalog service URL, client credentials, or trusted tenant header is sent to the
browser.

Copy `.env.example` to `.env.local`, provide the Keycloak client secret and a
unique `NEXTAUTH_SECRET`, then configure the Keycloak `teck-dashboard` client
for the local callback `http://localhost:3000/api/auth/callback/keycloak` and
Catalog audience/scope. Configure the dashboard's callback URI and web origin
in Keycloak for the URL that will serve this app. Use a dashboard client token
with the `catalog:manage` and `catalog:product-read` permissions and authorized
organization membership; Catalog and Keycloak remain authoritative. Set
`CATALOG_API_BASE_URL` to the public YARP base URL (the default is
`http://localhost:8080`); YARP matches the `/products` paths directly. Never
use a direct Catalog endpoint or a service credential in this setting.
Expired Keycloak access tokens are refreshed server-side using the stored refresh
token. If Keycloak rejects refresh, the dashboard requires staff sign-in again.
Set `DASHBOARD_ORIGIN` to the exact public origin (scheme, host and port) used
by `NEXTAUTH_URL`; deployments behind a reverse proxy must set it explicitly so
untrusted `Host` and forwarded headers are never used as the trust policy.
Cookie-authenticated product creation requires an exact matching `Origin`.

From the repository root, run `bunx nx serve web-dashboard` to start the app.
Successful creation is shown as confirmed only after a second, authorized GET
through YARP returns the product. A validation, authorization, network, or
read-back failure is shown as an error, not a successful confirmation.

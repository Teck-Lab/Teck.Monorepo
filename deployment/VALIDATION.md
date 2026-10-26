# Customer/admin deployment adaptation � 2026-10-26

Replace the retired Gateway.Public pilot with Gateway.WebCustomer and Gateway.WebAdmin from main fc31724ea7762b4a8215cb55f72b5853745fcdb5. Each has a separate build target, image, Rollout, ServiceAccount, stable/canary Services, HTTPRoute, OAuth client/secret and initial analysis. Local uses one replica per gateway; production bases retain two.

The local customer route uses the worktree host root. The admin route uses /admin with a Gateway API prefix rewrite to the existing application routes. Non-local environments retain distinct placeholder ingress hosts for GitOps to supply.

Keycloak's local template reuses canonical realm/client policy while generating separate web-customer-gateway/web-admin-gateway clients. The helper generates independent secrets and rejects incompatible retained credential formats. Retained existing realms need explicit migration; no data is deleted automatically. This is a companion change in GitOps and the native preview (0.1.7).

Portable deployment contracts were red against the old pilot and now pass against the current-main source snapshot. Companion regressions cover independent client credentials and Rollouts numeric-string observedGeneration. The latter is required by the controller API and fixes the previously unvalidated direct-apply Rollout path.

Current source was reconstructed from the local read-only Git archive plus GitHub App MCP directory/file reads, reusing local blobs where hashes match. Updated source blobs were checked against GitHub hashes. No personal remote Git transport or gh credentials were used, and the user's older local checkout was not reset or updated.

Live real-image build/deployment validation used a new owned disposable sandbox. This is not full commerce, user-token exchange, production mesh or HA acceptance.

Final result: PASS. Both real current-main .NET gateway images built with the shared Containerfile, loaded directly into KIND and deployed with kubectl. Both canary analyses were Successful. Customer /alive and rewritten admin /admin/alive returned 200 Healthy over trusted HTTPS. Their protected customer/admin paths returned 401 without credentials. The customer gateway returned 404 for the admin API path. Both distinct Keycloak client-credentials checks succeeded, using separate generated secrets. No Argo CD/Kargo/Git delivery services were installed.

Validation: four deployment contract tests passed against the current-main snapshot, 127 root tests passed in the preserved working checkout, and 126 root plus 21 native tests passed in the scoped PR snapshot. Build/check suites passed. The disposable gateway test sandbox was removed; existing project sandboxes were preserved.

This removes the retired-project build/manifest blocker. It does not establish a complete authenticated commerce workflow: downstream Catalog/Pricing/Customer services, real user-token exchange/roles and production mesh remain separate application integration coverage.

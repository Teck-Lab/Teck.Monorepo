# Application deployments

Source repos own deployment/base and local/development/staging/prod overlays. GitOps supplies cluster/operator configuration and operational overrides; clusters can consume the same environment overlay. Existing deploy/ resources remain during migration.

Local uses the official KIND workflow: build the worktree image, kind load docker-image into the project cluster, render deployment/local and kubectl apply. teck-kube deploy wraps those steps, loading content-based image tags, applying into the owned namespace, checking readiness and running declared initial analyses. No local Argo CD, Kargo, Git server, registry or promotion destination is involved. Deployment/local/builds.json lists images, optional controller add-ons and checks. Applications can use direct kubectl instead.

Each worktree owns its namespace/databases/dependencies. Platform operators are shared and explicitly enabled: cnpg for PostgreSQL integration, rollouts for Rollout/AnalysisTemplate, gateway for HTTPRoute/HTTPS. Test actual behavior, migrations and connectivity using the repo's tests or Kubernetes Jobs. A failed Job/readiness/analysis fails verification. Cleaning a worktree preserves other environments and shared operators.

The pilot deploys both Gateway.WebCustomer and Gateway.WebAdmin from the current web-customer/web-admin projects using their distinct image names and Keycloak clients (web-customer-gateway and web-admin-gateway). Each has its own Rollout, stable/canary Services, ServiceAccount, HTTPRoute and health analysis. The shared Containerfile already handles these two client-only BFFs.

Locally the customer gateway uses the worktree host's root path. The admin gateway uses /admin, with a Gateway API prefix rewrite before reaching its unchanged application routes. For example, /admin/alive reaches the admin liveness endpoint, and /admin/api/v1/admin/catalog/... reaches its operator routes. Outside local, GitOps assigns separate ingress hosts/parents; no cluster inventory is embedded here.

The optional local Keycloak renders separate clients from the canonical realm/client policy definitions. The helper creates independent customer-client-secret and admin-client-secret values in gateway-web-auth. Each gateway gets only its own value. Initial analyses verify each canary's liveness, Keycloak discovery, its own client credentials and a 401 response on its protected API path. A client-credentials token is not a substitute for a user session or a proof of a complete authenticated business workflow.

Run node --test deployment/tests/gateways.test.mjs for the deployment contract tests. The API server dry-run and live sandbox checks additionally validate rendered resources. Gateway build/source compatibility is checked against current main; the pilot still does not deploy all downstream Catalog/Pricing/Customer services.

Local kindnet has no Cilium/Istio ambient. Production mesh, HA, GitOps synchronization and Kargo promotion need the full environment. Terraform is outside this work.

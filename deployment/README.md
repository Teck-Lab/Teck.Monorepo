# Application deployments

Source repos own deployment/base and local/development/staging/prod overlays. GitOps supplies cluster/operator configuration and operational overrides; clusters can consume the same environment overlay. Existing deploy/ resources remain during migration.

Local uses the official KIND workflow: build the worktree image, kind load docker-image into the project cluster, render deployment/local and kubectl apply. teck-kube deploy wraps those steps, loading content-based image tags, applying into the owned namespace, checking readiness and running declared initial analyses. No local Argo CD, Kargo, Git server, registry or promotion destination is involved. Deployment/local/builds.json lists images, optional controller add-ons and checks. Applications can use direct kubectl instead.

Each worktree owns its namespace/databases/dependencies. Platform operators are shared and explicitly enabled: cnpg for PostgreSQL integration, rollouts for Rollout/AnalysisTemplate, gateway for HTTPRoute/HTTPS. Test actual behavior, migrations and connectivity using the repo's tests or Kubernetes Jobs. A failed Job/readiness/analysis fails verification. Cleaning a worktree preserves other environments and shared operators.

The gateway-public pilot requires rollouts and gateway plus a per-worktree Keycloak from GitOps apps/local/security/keycloak. It uses realm teck-cloud-local and client teck-public-gateway, rendered from canonical existing realm/client definitions with generated credentials. This is not the whole commerce stack.

Draft compatibility blocker: this pilot targets an older checkout; current main retired Gateway.Public. Adapt the pilot/build declaration to the current gateways before merge. A synthetic app/database fixture validates the shared KIND/CNPG workflow separately and does not establish current-main gateway compatibility.

Local kindnet has no Cilium/Istio ambient. Production mesh, HA, GitOps synchronization and Kargo promotion need the full environment. Terraform is outside this work.

# Application deployments

The gateway-public pilot uses base/ plus local/, development/, staging/ or prod/.
GitOps supplies concrete clusters, ingress parents/hostnames, configuration and
secrets. Several clusters can consume the same prod overlay. These new overlays
are not registered with any remote cluster by this change. Existing deploy/
resources are retained during incremental migration.

In an enabled Orca project, run teck-kube deploy from the current worktree. The
helper builds the declared image with the shared Containerfile, records dirty
source content, injects immutable image references and a worktree namespace/host,
and synchronizes local Argo CD. No GitHub push is needed. teck-kube promote tests
a separate local delivery destination; it never promotes a remote release.

The local gateway uses a per-worktree Keycloak dependency supplied by the local
GitOps overlay at apps/local/security/keycloak and a generated client secret.
That overlay renders the existing in-cluster development realm/client definitions;
the local realm is teck-cloud-local and the client is teck-public-gateway. Health and unauthenticated request
checks cover that pilot; order/pricing/customer services are not yet included,
so this does not establish a complete authenticated commerce workflow. Add other
services/dependencies as their deployment resources migrate.

Workspace tests use kindnet with no mesh. Mesh/security changes require the
optional full target, once its Cilium/ambient compatibility has been validated.
Do not apply the base directly: it intentionally needs an image digest, ingress
configuration, environment settings and Secret references supplied by its target.

The canonical GitOps layout was validated separately with a synthetic two-worktree
app, real Keycloak token authentication, local Kargo promotion and cold restart.
That fixture validates the shared platform; it does not validate this pilot against
current main, where Gateway.Public has been retired. Adapt the pilot before merge.

# Application deployment manifests

This is the application's Kubernetes source: base + local/development/staging/prod.
Keep cluster inventory, platform/controller installation and cluster-specific
operational overrides in Teck.GitOps. Both workspace and full local targets consume
the same local overlay. Terraform is outside this subtree.

The first pilot covers gateway-public. Existing deploy/ Dockerfiles and service
manifests remain in use; this is not a claim that all services have migrated.
Use the shared deploy/Containerfile.template and the repo's normal build/tests.

Declare application images by canonical name; the local helper injects the exact
worktree image digest. Released image/manifest pairs are managed together by
GitOps/Kargo. Never publish a mutable latest image for the local release path.
Gateway parent references/hostnames and secrets are supplied operationally.
Keep authentication enabled; generate local credentials separately and reference
Secrets rather than storing their values in deployment resources.

Rollouts owns backend weights. Argo CD must ignore only those weights and pass
RespectIgnoreDifferences=true in policy and direct sync operations. Analysis must
check real behavior; do not replace health/dependency analysis with true/sleep.

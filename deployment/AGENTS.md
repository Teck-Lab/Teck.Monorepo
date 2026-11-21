# Application deployment manifests

This is the application's Kubernetes source: base + local/development/staging/prod.
Keep cluster inventory, platform/controller installation and cluster-specific
operational overrides in Teck.GitOps. Both workspace and full local targets consume
the same local overlay. Terraform is outside this subtree.

The pilot covers gateway-web-customer and gateway-web-admin. Existing deploy/ Dockerfiles and service
manifests remain in use; this is not a claim that all services have migrated.
Use the shared deploy/Containerfile.template and the repo's normal build/tests.

Declare application images by canonical name; the local helper injects the exact
worktree content-based image tag (loaded directly into KIND). Released image/manifest pairs are managed together by
GitOps/Kargo. Never use a mutable latest tag for local tests.
Gateway parent references/hostnames and secrets are supplied operationally.
Keep authentication enabled; generate local credentials separately and reference
Secrets rather than storing their values in deployment resources.

Local agents use Docker build, kind load docker-image and kubectl apply against the local overlay. Local does not run Argo CD or Kargo. Keep the Rollouts controller when using Rollout resources, and a Gateway API controller for routes. Use the optional CNPG operator with an isolated Cluster per worktree for database integration tests. Keep controller-owned backend weights/service selectors undeclared or stable under normal three-way apply. Analysis and test Jobs must check actual behavior. Deployment/local/builds.json addons declares required shared controllers.

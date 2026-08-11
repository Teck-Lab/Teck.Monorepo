# Local GitHub automation credentials

This directory is mounted read-only at `/run/secrets/teck-github` in the dev
container and every Orca local workspace container. Credential files here are
gitignored and are never copied into an image.

## GitHub App MCP authentication

1. Copy `github-app.env.example` to `github-app.env` and fill in the App ID and
   installation ID.
2. Download an RSA private key from the GitHub App settings and save it as
   `github-app.pem`.
3. Install the App only on the required repository. Grant Metadata read,
   Contents read/write, Issues read/write, Pull requests read/write, and Actions
   read. Contents write is used only by the coordinator's Git push wrapper;
   startup fetch tokens are explicitly down-scoped to Contents read.
   Checks, Commit statuses, Code quality, Code scanning alerts, Dependabot
   alerts, and Secret scanning alerts may be read-only so review agents can
   inspect CI and security findings when corresponding MCP tools are enabled.
   Leave Deployments disabled. Do not grant ruleset bypass, repository
   administration, security-alert dismissal, review submission, or PR merge.

The MCP launcher exposes issue/sub-issue management, PR creation/update, and
read-only repository/CI tools. It deliberately does not expose GitHub-side file
commits, branch creation, workflow dispatch, review submission, or PR merge.

## Commit identity and signing

Worker checkpoint commits stay local and may be unsigned. When a sub-worktree
is complete, `tools/orca-feature integrate` promotes its exact tree as one
GitHub App-authored commit. GitHub signs that commit with `web-flow`; no GPG
private key is mounted into the container.

## Proton Pass provider for Orca

The Orca local recipe can retrieve these credentials through `pass-cli` on WSL
instead of keeping generated files here:

1. Install Proton Pass CLI on WSL.
2. Store the GitHub App and five upstream LiteLLM provider
   credentials in Proton Pass. A separate GitHub PAT is not needed; short-lived
   App installation tokens authenticate Git. The workspace-local
   Provider credentials are injected directly into the workspace environment.
3. Create a Proton PAT with viewer access only to those items.
4. Copy `proton-pass.env.example` to the gitignored `proton-pass.env` and
   replace its `pass://` references with vault/item IDs and field names.
5. Save the Proton PAT as `~/.config/teck-orca/proton-pass.pat` with mode
   `0600`, or supply `PROTON_PASS_PERSONAL_ACCESS_TOKEN` when creating.

When `proton-pass.env` exists, creation fails closed unless every required
reference resolves. The hook creates an isolated Proton session and selected
runtime files under WSL `/dev/shm`, logs Proton Pass out immediately, mounts
only GitHub App files plus direct provider credentials, and mints a one-hour App
installation token only for each Git operation.
Destroy removes both the container and its associated tmpfs directory. Without
that config, the existing local-file credential directory remains the fallback.

`private-key-pem` is the complete GitHub App `.pem` file.

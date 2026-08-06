# Orca local per-workspace environment

This recipe builds on the repository's `.devcontainer/devcontainer.json`, adds
an SSH transport, and launches one disposable Docker container per Orca
workspace. No authentication is copied or committed into an image. The recipe
mounts the same persistent `codex-config-*` and `opencode-data-*` Docker volumes
declared by the dev container. Codex's WSL-hosted `~/.codex/auth.json` is mounted
as a single read/write file so refreshes persist without exposing or copying the
rest of the host Codex directory.

GitHub App credentials and the WSL2-generated automation signing-key export are
mounted read-only from `.devcontainer/github-app` at runtime. They are not
present during the base build and are never committed into the image. Startup
imports the signing key into the disposable container's writable GPG home.

Alternatively, copy `.devcontainer/github-app/proton-pass.env.example` to
`proton-pass.env` and configure its `pass://` references. The WSL-side create
hook then uses a narrowly scoped Proton PAT to retrieve the GitHub App and
signing material plus the five upstream LiteLLM credentials into `/dev/shm`.
The local LiteLLM master key is generated independently for every workspace.
Git uses short-lived installation tokens minted from the App, so no separate
GitHub PAT is stored. Proton CLI and its PAT stay outside the container; only
selected runtime files are mounted read-only. The local-file flow remains
available when the Proton configuration is absent.

The lifecycle commands in `orca.yaml` bridge Orca Desktop on Windows directly
into this WSL checkout. This avoids `cmd.exe`, which cannot use a WSL UNC path
as its working directory. Update the `--distribution` or `--cd` values if the
checkout moves or the WSL distro name changes.

## One-time setup

1. Build the reusable base (this runs the dev-container build and repo setup):
   `./scripts/orca-vm/local-build-base.sh`
   The build snapshots the current local branch, including commits that have
   not been pushed. Use `ORCA_SOURCE_REF=main ./scripts/orca-vm/local-build-base.sh`
   after merging locally to make local `main` the environment baseline.
   Set `ORCA_FETCH_REMOTE=1` during creation only when the recorded snapshot
   should deliberately be replaced with the configured remote ref.
2. Ensure Codex and OpenCode are signed in through the normal dev container.
3. Validate provisioning:
   `orca-ide vm recipe doctor local-devcontainer --repo-path . --provision --json`

The transport key is generated with Windows OpenSSH under `%USERPROFILE%\.ssh`
so its ACL is accepted by Orca Desktop. Local recipe state is gitignored. If
Docker has more than one matching volume, set `ORCA_CODEX_VOLUME` and
`ORCA_OPENCODE_VOLUME` explicitly. Re-run the base build after material
dev-container changes.

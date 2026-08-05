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

The lifecycle commands in `orca.yaml` bridge Orca Desktop on Windows directly
into this WSL checkout. This avoids `cmd.exe`, which cannot use a WSL UNC path
as its working directory. Update the `--distribution` or `--cd` values if the
checkout moves or the WSL distro name changes.

## One-time setup

1. Build the reusable base (this runs the dev-container build and repo setup):
   `./scripts/orca-vm/local-build-base.sh`
2. Ensure Codex and OpenCode are signed in through the normal dev container.
3. Validate provisioning:
   `orca-ide vm recipe doctor local-devcontainer --repo-path . --provision --json`

The transport key is generated with Windows OpenSSH under `%USERPROFILE%\.ssh`
so its ACL is accepted by Orca Desktop. Local recipe state is gitignored. If
Docker has more than one matching volume, set `ORCA_CODEX_VOLUME` and
`ORCA_OPENCODE_VOLUME` explicitly. Re-run the base build after material
dev-container changes.

# Orca local per-workspace environment

This recipe builds on the repository's `.devcontainer/devcontainer.json`, adds
an SSH transport, and launches one disposable Compose project per Orca
workspace. No authentication is copied or committed into an image. The recipe
mounts the same persistent `codex-config-*` and `opencode-data-*` Docker volumes
declared by the dev container. Codex's WSL-hosted `~/.codex/auth.json` is mounted
as a single read/write file so refreshes persist without exposing or copying the
rest of the host Codex directory.

Copy `.devcontainer/github-app/proton-pass.env.example` to the gitignored
`proton-pass.env` and configure its `pass://` references. Only that reference
file and the narrowly scoped PAT at `~/.config/teck-orca/proton-pass.pat` are
mounted read-only. The pinned Proton CLI in the image materializes GitHub App
and direct-provider credentials into container tmpfs at every start, then logs
out and removes its session. Git uses short-lived installation tokens minted
from the App, so no separate GitHub PAT is stored.

The repository lives in the disposable workspace container's writable layer,
which survives container, WSL2, Docker, and host restarts without forcing
Docker to copy the prepared checkout into a new volume during Orca's handshake.
The nested Docker daemon and containerd state use per-workspace named volumes.
All Compose services use Docker restart policies, so Docker Desktop can restore
them after a machine restart without a WSL system service or Windows scheduled
task. The SSH container and published port remain the same, allowing Orca to
reconnect to its existing recipe result. Deliberately deleting the Orca
workspace removes the containers and per-workspace Docker volumes.

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
2. Put the Proton PAT at `~/.config/teck-orca/proton-pass.pat` (mode `0600`) and
   ensure Codex and OpenCode are signed in once in WSL2.
3. Validate provisioning:
   `orca-ide vm recipe doctor local-devcontainer --repo-path . --provision --json`

The transport key is generated with Windows OpenSSH under `%USERPROFILE%\.ssh`
so its ACL is accepted by Orca Desktop. Local recipe state is gitignored. If
Docker has more than one matching volume, set `ORCA_CODEX_VOLUME` and
`ORCA_OPENCODE_VOLUME` explicitly. Re-run the base build after material
dev-container changes.

# Orca local per-workspace environment

The Orca recipe launches every parent-feature workspace with the official Dev
Container CLI. `devcontainer up` reads `.devcontainer/devcontainer.json` from
that workspace's checkout, resolves its Features, builds when required, and
runs its lifecycle commands. There is no separate Orca base image and no manual
base rebuild step.

The CLI is pinned to `@devcontainers/cli@0.88.0` in the launcher so an upstream
CLI release cannot silently alter provisioning behavior.

Each parent feature gets one disposable host checkout and one Dev Container.
Orca connects to it over the SSH transport included in the normal dev-container
image. Native Orca child worktrees and their OMO workers live inside that same
feature environment. VS Code can attach to the running container and sees the
same definition, tools, mounts, extensions, and workspace folder.

The lifecycle commands in `orca.yaml` bridge Orca Desktop on Windows into WSL.
Update the distribution or checkout path there if either changes.

## One-time setup

1. Put the Proton Pass CLI PAT at
   `~/.config/teck-orca/proton-pass.pat` with mode `0600`.
2. Keep the existing gitignored Proton references in
   `.devcontainer/github-app/proton-pass.env` and MCP references in
   `.devcontainer/mcp/mcp.env`.
3. Sign in to Codex and OpenCode once in WSL so their existing auth files can
   be mounted into each Dev Container.
4. Validate the recipe statically with
   `orca-ide vm recipe doctor local-devcontainer --repo-path . --json`.

For a live validation, add `--provision`; this creates and destroys a real
Dev Container. Future changes merged into the selected repository ref are
automatically applied the next time Orca creates a workspace. Docker's normal
build cache keeps unchanged Features and Dockerfile layers fast.

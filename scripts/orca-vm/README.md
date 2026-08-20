# Orca local per-workspace environment

The Orca recipe launches each parent-feature workspace with the official Dev
Container CLI. It reads `.devcontainer/devcontainer.json` from current `main`,
resolves Features, builds when required, and runs lifecycle commands. There is
no separate manually rebuilt Orca base image.

Each parent feature gets one disposable host checkout and one Dev Container.
Orca connects through the container's SSH transport. Native Orca child
worktrees and dedicated Codex workers live inside that feature environment; a
child never provisions another recipe environment.

The Dev Container CLI is pinned to `@devcontainers/cli@0.88.0`. Changes merged
to the devcontainer definition apply automatically to the next workspace;
Docker reuses unchanged build layers.

## One-time setup

1. Keep the gitignored MCP references in `.devcontainer/mcp/mcp.env`.
2. Sign in to GitHub CLI and Codex once in WSL so their auth files can be
   mounted read-only into each workspace.
3. Validate statically with
   `orca-ide vm recipe doctor local-devcontainer --repo-path . --json`.
4. Add `--provision` for a live create/destroy validation.

No Proton Pass, OpenCode, DeepSeek, OpenRouter, or provider-key bootstrap is
required by this recipe.

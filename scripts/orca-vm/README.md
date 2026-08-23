# Orca local per-workspace environment

The Orca recipe launches each parent-feature workspace with the official Dev
Container CLI. It reads `.devcontainer/devcontainer.json` from current `main`,
resolves Features, builds when required, and runs lifecycle commands. There is
no separate manually rebuilt Orca base image.

Each parent feature gets one disposable host checkout and one Dev Container.
The recipe returns Orca's default schema-v1 SSH contract. Orca keeps the
checkout at `projectRoot` as the host's primary repository and creates the
assigned task as a linked worktree. This keeps task deletion scoped to the
worktree instead of removing the owning project. The native startup draft
delivers a linked GitHub issue to the first agent terminal, so no separate
`issueCommand` terminal is configured.

The Compose project name is stable for `ORCA_VM_INSTANCE_ID`. Paired suspend
and resume hooks stop or restart the existing containers, rediscover the
published SSH port, verify SSH, and re-emit fresh connection JSON. Repeated
create for the same instance resumes it instead of provisioning a duplicate.
Every resume re-emits the schema-v1 SSH result with the current forwarded port.

The Dev Container CLI is pinned to `@devcontainers/cli@0.88.0`. Changes merged
to the devcontainer definition apply automatically to the next workspace;
Docker reuses unchanged build layers.

## One-time setup

1. In Orca Settings → Agents, select Claude Code as the default agent for new
   workspaces and set its launch arguments to `--model claude-opus-5 --effort
   high`. This is an Orca user setting; `orca.yaml` cannot select the initial
   workspace agent. Keep Codex authenticated as the documented
   `gpt-5.6-sol`/high fallback.
2. Keep the gitignored MCP references in `.devcontainer/mcp/mcp.env`.
3. Rebuild/open one devcontainer, run `claude`, and complete sign-in once. The
   official Claude Code feature is installed in every workspace and the shared
   `claude-code-config` Docker volume persists its authentication and settings.
   Keep GitHub CLI and Codex signed in through their existing WSL mounts.
4. Validate statically with
   `orca-ide vm recipe doctor local-devcontainer --repo-path . --json`.
5. Add `--provision` for a live create/destroy validation.

No Proton Pass, OpenCode, DeepSeek, OpenRouter, or provider-key bootstrap is
required by this recipe.

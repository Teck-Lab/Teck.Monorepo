# Teck development container

The development environment is a Docker Compose application. The `workspace`
service is opened by Dev Containers and Orca; SearXNG and Crawl4AI remain
available as shared research backends.

`devcontainer.json` combines the workspace Compose file with the MCP services.
Its initialize command prepares their gitignored settings and token. Orca's
`local-devcontainer` recipe adds the isolated checkout, random SSH port, and
agent/GitHub authentication persistence.

## Agent runtime

- Orca owns Runs, Tasks, Dispatches, worktrees, dependencies, and completion.
- Claude Opus 5/high is the preferred parent coordinator. Codex Sol/high is the
  availability fallback; Orca owns the handoff and allows only one live parent
  coordinator.
- Model-routed native Orca workers own planning, plan review, execution,
  coherent-unit code review, consolidation, and whole-feature QA.
- Oh My Codex 0.20.5 is installed during the image build and configured at
  user scope for native roles, skills, and hooks.
- OMX worktrees, teams, tmux launchers, autopilot, Ralph, and goal ledgers are
  not used because they would duplicate Orca lifecycle ownership.
- Native Codex subagents may be used inside an executor's assigned worktree and
  remain bounded by that GitHub sub-issue.

Claude Code and Codex use authentication mounted from WSL2. Claude's mutable
non-auth state remains isolated in a per-devcontainer volume. GitHub MCP and Git
use the mounted GitHub CLI session. No OpenCode or provider-key bootstrap is
required.

Run `teck-runtime-doctor` inside a workspace to verify Claude Code, Codex, OMX,
GitHub MCP, Git identity, research services, Docker, and the repository checkout
without printing credentials.

## Docker and validation

The workspace remains privileged with Docker-in-Docker for Testcontainers,
Aspire, and integration tests. Common commands are:

```bash
bun install
dotnet restore Teck.Platform.slnx
nx affected -t build test lint typecheck
```

Never run `nx release` or create tags from a feature branch. Changes to the
Dockerfile, Features, Compose files, or post-create setup apply automatically
when the next Orca workspace is created through the Dev Container CLI.

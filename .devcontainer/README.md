# Teck development container

The development environment is a Docker Compose application. The `workspace`
service is opened by Dev Containers and Orca; SearXNG and Crawl4AI remain
available as shared research backends.

`devcontainer.json` combines the workspace Compose file with the MCP services.
Its initialize command prepares their gitignored settings and token. Orca's
`local-devcontainer` recipe adds the isolated checkout, random SSH port, and
Codex/GitHub authentication mounts.

## Agent runtime

- Orca owns Runs, Tasks, Dispatches, worktrees, dependencies, and completion.
- Native Codex owns coordinator, planner, plan-reviewer, executor, code-review,
  and final-QA sessions.
- Oh My Codex 0.20.5 is installed during the image build and configured at
  user scope for native roles, skills, and hooks.
- OMX worktrees, teams, tmux launchers, autopilot, Ralph, and goal ledgers are
  not used because they would duplicate Orca lifecycle ownership.
- Native Codex subagents may be used inside an executor's assigned worktree and
  remain bounded by that GitHub sub-issue.

Codex uses the OpenAI authentication mounted from WSL2. GitHub MCP and Git use
the mounted GitHub CLI session. No OpenCode or third-party model provider
credentials are required.

Run `teck-runtime-doctor` inside a workspace to verify Codex, OMX, GitHub MCP,
Git identity, research services, Docker, and the repository checkout without
printing credentials.

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

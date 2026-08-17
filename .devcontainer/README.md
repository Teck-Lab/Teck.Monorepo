# Teck development container

The development environment is a Docker Compose application. The `workspace`
service is the container opened by Dev Containers and Orca; `searxng` and
`crawl4ai` are sibling services used by OpenCode research agents.

## Start it

Open the repository in a Dev Containers client. `devcontainer.json` merges:

- `compose.yaml` — the workspace service, local mounts, and shared secrets;
- `mcp/compose.yaml` — SearXNG and Crawl4AI.

`initializeCommand` runs `prepare-compose.sh` before Compose. It renders the
gitignored SearXNG settings and generates the Crawl4AI token.

Orca's `local-devcontainer` recipe launches the same Compose topology with a
per-workspace override for the isolated repository, random SSH port, persistent
agent auth volumes, and tmpfs-backed secrets.

## AI routing

OMO routes the ordinary Sisyphus agent through Kimi K2.7 Code on OpenCode Go 1,
then the same model on OpenCode Go 2, with GPT-5.6 Sol as its final fallback. The
orchestrated planner, executors, specialists, and every category remain GPT-5.6
routed: Luna handles quick lookup work, Terra handles standard work, and Sol
handles deep planning and implementation.

OpenCode Go subscriptions 1 and 2, direct DeepSeek, and OpenRouter remain
registered for explicit use. Their four credentials are resolved from Proton
Pass by the Orca recipe and mounted read-only at runtime; they are not OMO
defaults and never live under `.devcontainer`.

Codex uses the OpenAI authentication mounted from WSL2 and does not require a
provider-key file or an in-container login.

## Internet research

Models do not browse by themselves. OMO research agents use:

- SearXNG at `http://searxng:8080` through `mcp-searxng`;
- Crawl4AI at `http://crawl4ai:11235/mcp/sse` with its generated bearer token;
- Context7 as a remote documentation MCP.

The workspace SSH transport starts in parallel with the research services so
Orca can complete its provisioning handshake promptly. Research agents should
use `teck-runtime-doctor` when service readiness matters; Compose continues to
health-check both backends independently.

Run `teck-runtime-doctor` inside a workspace to verify agent authentication,
GitHub CLI access, Git identity and transport, bounded OMO routing, research
services, the publication policy, and tmux attachment without printing
credentials.

## Worker flow

Full OMO is the default OpenCode harness. Prometheus and Atlas handle planned
work; Hephaestus handles explicitly autonomous/spike work. `teck-omo-worker`
creates the tmux session for an Orca sub-issue. Nested agents may inspect,
research, edit, test, and review only within the assigned worktree; the primary
worker owns Git and Orca lifecycle messages.

## Authentication and persistence

- OpenCode and Codex auth files are mounted read-only from WSL2.
- Their writable state lives in named Docker volumes.
- GitHub MCP and Git use the GitHub CLI session mounted from WSL2.
- Workers never push child branches; the coordinator pushes the integrated
  parent feature branch.

## Docker and tests

The workspace remains privileged with Docker-in-Docker for Testcontainers,
Aspire, and integration tests. The AI and MCP services are not started through
that nested daemon—they are Compose siblings managed by the host.

Common commands:

```bash
bun install
dotnet restore Teck.Platform.slnx
nx affected -t build test lint typecheck
```

Never run `nx release` or create tags from a feature branch.

## Rebuild requirements

Changes to `devcontainer.json`, Compose files, features, or the Dockerfile
require rebuilding/recreating the workspace. Configuration changes under
`.devcontainer/opencode/` are reseeded by `postCreate.sh` during that rebuild.

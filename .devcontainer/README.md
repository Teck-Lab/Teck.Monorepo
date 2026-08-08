# Teck development container

The development environment is a Docker Compose application. The `workspace`
service is the container opened by Dev Containers and Orca; `searxng` and
`crawl4ai` are sibling services used by OpenCode research agents.

## Start it

Open the repository in a Dev Containers client. `devcontainer.json` merges:

- `compose.yaml` — the workspace service and service dependencies;
- `compose.devcontainer.yaml` — the local source mount;
- `mcp/compose.yaml` — SearXNG and Crawl4AI.

`initializeCommand` runs `prepare-compose.sh` before Compose. It renders the
gitignored SearXNG settings, generates the Crawl4AI token, and obtains direct AI
provider credentials from Proton Pass when `ai/providers.env` is absent.

Orca's `local-devcontainer` recipe launches the same Compose topology with a
per-workspace override for the isolated repository, random SSH port, persistent
agent auth volumes, and tmpfs-backed secrets.

## AI routing

There is no local model gateway. OpenCode registers each upstream directly:

- `opencode-go-a` and `opencode-go-b` for the two Go subscriptions;
- `opencode-zen` for free Zen routes;
- `nvidia`, `deepseek`, and `openrouter` for their direct APIs;
- `openai` for the GPT fallback and primary planning/execution agents.

Full OMO owns ordered fallback. Cheap Explore/Librarian work starts with Go A,
then Go B, Zen, NVIDIA, DeepSeek, and finally GPT. This is failover rather than
rate-aware load balancing.

Provider secrets are injected as environment variables from the gitignored
`ai/providers.env`, or from a per-workspace tmpfs file created through Proton
Pass. Copy `ai/providers.env.example` only when configuring without Proton.

Codex uses the OpenAI authentication mounted from WSL2 and does not require a
provider-key file or an in-container login.

## Internet research

Models do not browse by themselves. OMO research agents use:

- SearXNG at `http://searxng:8080` through `mcp-searxng`;
- Crawl4AI at `http://crawl4ai:11235/mcp/sse` with its generated bearer token;
- Context7 as a remote documentation MCP.

Compose health checks must pass before the workspace starts, so a research
agent cannot silently launch without its local browsing services.

## Worker flow

Full OMO is the default OpenCode harness. Prometheus and Atlas handle planned
work; Hephaestus handles explicitly autonomous/spike work. `teck-omo-worker`
creates the tmux session for an Orca sub-issue. Nested agents may inspect,
research, edit, test, and review only within the assigned worktree; the primary
worker owns Git and Orca lifecycle messages.

## Authentication and persistence

- OpenCode and Codex auth files are mounted read-only from WSL2.
- Their writable state lives in named Docker volumes.
- GitHub MCP uses the repository GitHub App files mounted at
  `/run/secrets/teck-github`.
- Provider keys and signing material are loaded from Proton Pass and are never
  baked into the image.
- Completed worker trees are promoted by the GitHub App flow; they are not
  pushed directly.

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

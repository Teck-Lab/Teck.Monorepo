# Dev container base image for the Teck monorepo.
#
# We build from a Dockerfile (rather than referencing the image directly in
# devcontainer.json) for one reason: to pre-create ~/.claude-config owned by the
# non-root `vscode` user BEFORE the named config volume is mounted there.
#
# When Docker mounts a fresh named volume onto a path that already exists in
# the image, it copies that path's ownership into the new volume. If the path
# does not exist, Docker creates the volume owned by root, and Claude Code —
# which runs as `vscode` — then cannot write .credentials.json, so
# the browser sign-in reports success but never persists ("not signed in").
# Creating and chowning the directory here makes the volume vscode-owned and
# writable, mirroring what the official Claude Code reference container does.
#
# The path is ~/.claude-config, not ~/.claude, because devcontainer.json sets
# CLAUDE_CONFIG_DIR to it. That relocates the ENTIRE Claude state tree —
# critically including ~/.claude.json, which holds the OAuth session and which
# is a SIBLING of ~/.claude, not inside it. Mounting only ~/.claude left
# .claude.json on the container's ephemeral filesystem, so every rebuild wiped
# it and Claude Code demanded a fresh sign-in even though the token had
# persisted. One env var + one mount now covers both.
#
# All other tooling (.NET, Docker-in-Docker, Bun, Node, Claude Code, GitHub
# CLI) is still layered on via the `features` block in devcontainer.json.
FROM mcr.microsoft.com/devcontainers/dotnet:2.1-10.0-noble

# tmux — one attached session per Orca sub-issue worker. Full OMO spawns nested
# agents into panes in that session. teck-omo-worker supplies the explicit
# OpenCode port required by pane attachment. (System package, not a feature.)
RUN apt-get update \
    && apt-get install -y --no-install-recommends tmux \
    && rm -rf /var/lib/apt/lists/*

# snip — required by the `opencode-snip` OpenCode plugin, which prefixes shell
# commands with `snip` to filter their output before it reaches the model
# context (the project claims 60-90% fewer tokens on noisy commands). The plugin
# is declared in opencode.json's `plugin` array and shells out to this binary, so
# without it on PATH the plugin is inert.
#
# Installed from the upstream prebuilt release rather than `go install`: there is
# no Go toolchain in this image and adding one for a single static binary would
# be a large layer for no benefit. Pinned per the repo convention — never `latest`.
ARG SNIP_VERSION=0.22.0
RUN arch="$(dpkg --print-architecture)" \
    && case "$arch" in \
         amd64) snip_arch="amd64" ;; \
         arm64) snip_arch="arm64" ;; \
         *) echo "unsupported arch for snip: $arch" >&2; exit 1 ;; \
       esac \
    && curl -fsSL "https://github.com/edouard-claude/snip/releases/download/v${SNIP_VERSION}/snip_${SNIP_VERSION}_linux_${snip_arch}.tar.gz" \
       -o /tmp/snip.tar.gz \
    && tar -xzf /tmp/snip.tar.gz -C /tmp snip \
    && install -m 0755 /tmp/snip /usr/local/bin/snip \
    && rm -f /tmp/snip.tar.gz /tmp/snip \
    && snip --version

# Same rationale applies to OpenCode's data dir: ~/.local/share/opencode holds
# auth.json (the `opencode auth login` / ChatGPT-subscription credentials) and
# plugin state. Pre-create it vscode-owned so its named volume is writable and the
# login survives rebuilds — otherwise a root-owned volume silently loses the auth.
#
# ~/.codex gets the same treatment. Codex authenticates to the LiteLLM gateway via
# the LITELLM_MASTER_KEY env var today, so it has no stored credential — but a
# `codex login` (ChatGPT account) writes ~/.codex/auth.json, and without a volume
# that would silently vanish on every rebuild. postCreate.sh still re-seeds
# config.toml from the committed template on top of the volume, so the template
# stays the source of truth for configuration while auth persists.
# ~/.config/gh is on the same list: `gh auth login` writes its OAuth token to
# ~/.config/gh/hosts.yml, which is NOT under any other mounted path — so without
# its own volume every rebuild silently logged you out of GitHub. This login is
# retained for ordinary `gh`/Git transport use; GitHub MCP authenticates
# separately as the repository-scoped GitHub App.
RUN mkdir -p /home/vscode/.claude-config /home/vscode/.local/share/opencode \
      /home/vscode/.codex /home/vscode/.config/gh \
    && chown -R vscode:vscode /home/vscode/.claude-config /home/vscode/.local \
      /home/vscode/.codex /home/vscode/.config

# Official GitHub MCP server. It runs directly as a stdio child of Codex or
# OpenCode, avoiding a nested-Docker secret mount. Version and per-architecture
# checksums are pinned so rebuilds cannot silently pick up a different binary.
ARG GITHUB_MCP_VERSION=1.8.0
RUN arch="$(dpkg --print-architecture)" \
    && case "$arch" in \
         amd64) mcp_arch="x86_64"; mcp_sha="b2754921aec1b1302b19a71531d26d242ef0e7f1e05696b8444beab5a7e61d5b" ;; \
         arm64) mcp_arch="arm64"; mcp_sha="c4b0fe8d4e31c079e5c3f3a54050a08449dae6fa8189ec5998822555ad27bde8" ;; \
         *) echo "unsupported architecture for github-mcp-server: $arch" >&2; exit 1 ;; \
       esac \
    && archive="github-mcp-server_Linux_${mcp_arch}.tar.gz" \
    && curl -fsSL "https://github.com/github/github-mcp-server/releases/download/v${GITHUB_MCP_VERSION}/${archive}" -o "/tmp/${archive}" \
    && printf '%s  %s\n' "$mcp_sha" "/tmp/${archive}" | sha256sum -c - \
    && tar -xzf "/tmp/${archive}" -C /tmp github-mcp-server \
    && install -m 0755 /tmp/github-mcp-server /usr/local/bin/github-mcp-server \
    && rm -f "/tmp/${archive}" /tmp/github-mcp-server \
    && github-mcp-server --version

COPY github-mcp.sh /usr/local/bin/teck-github-mcp
COPY github-app-token.sh /usr/local/bin/teck-github-app-token
COPY git-with-github-app.sh /usr/local/bin/teck-git-with-github-app
COPY omo-worker.sh /usr/local/bin/teck-omo-worker
RUN chmod 0755 /usr/local/bin/teck-github-mcp \
      /usr/local/bin/teck-github-app-token /usr/local/bin/teck-git-with-github-app \
      /usr/local/bin/teck-omo-worker

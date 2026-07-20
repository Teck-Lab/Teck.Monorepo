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

# tmux — required by oh-my-opencode-slim's multiplexer integration: background
# subagents are spawned into live tmux panes, so tmux must be installed and on
# PATH. Launch OpenCode via the `omos` helper (see postCreate.sh) so it binds an
# explicit port — pane attachment needs a real TCP listener, which OpenCode's
# default `--port 0` does not provide. (System package → installed here, not via
# a feature.)
RUN apt-get update \
    && apt-get install -y --no-install-recommends tmux \
    && rm -rf /var/lib/apt/lists/*

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
RUN mkdir -p /home/vscode/.claude-config /home/vscode/.local/share/opencode /home/vscode/.codex \
    && chown -R vscode:vscode /home/vscode/.claude-config /home/vscode/.local /home/vscode/.codex

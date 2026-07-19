# Dev container base image for the Teck monorepo.
#
# We build from a Dockerfile (rather than referencing the image directly in
# devcontainer.json) for one reason: to pre-create ~/.claude owned by the
# non-root `vscode` user BEFORE the named config volume is mounted there.
#
# When Docker mounts a fresh named volume onto a path that already exists in
# the image, it copies that path's ownership into the new volume. If the path
# does not exist, Docker creates the volume owned by root, and Claude Code —
# which runs as `vscode` — then cannot write ~/.claude/.credentials.json, so
# the browser sign-in reports success but never persists ("not signed in").
# Creating and chowning the directory here makes the volume vscode-owned and
# writable, mirroring what the official Claude Code reference container does.
#
# All other tooling (.NET, Docker-in-Docker, Bun, Node, Claude Code, GitHub
# CLI) is still layered on via the `features` block in devcontainer.json.
FROM mcr.microsoft.com/devcontainers/dotnet:2.1-10.0-noble

# Same rationale applies to OpenCode's data dir: ~/.local/share/opencode holds
# auth.json (the `opencode auth login` / ChatGPT-subscription credentials) and
# plugin state. Pre-create it vscode-owned so its named volume is writable and the
# login survives rebuilds — otherwise a root-owned volume silently loses the auth.
RUN mkdir -p /home/vscode/.claude /home/vscode/.local/share/opencode \
    && chown -R vscode:vscode /home/vscode/.claude /home/vscode/.local

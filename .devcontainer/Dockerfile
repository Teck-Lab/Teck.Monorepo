# Dev container base image for the Teck monorepo.
#
# Runtime tooling is layered on via the `features` block in devcontainer.json;
# this Dockerfile supplies the OS, SSH relay, and pinned GitHub MCP binary.
FROM mcr.microsoft.com/devcontainers/dotnet:2.1-10.0-noble

# Orca's remote relay installs node-pty at connection time. Node releases that
# do not have a matching node-pty prebuild fall back to node-gyp, which requires
# a complete Python standard library (including shlex) and a native compiler.
# The Node devcontainer feature can leave only python3-minimal installed, so
# keep these dependencies explicit instead of relying on feature internals.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
      build-essential \
      openssh-server \
      python3 \
      python3-dev \
    && rm -rf /var/lib/apt/lists/*

RUN mkdir -p /run/sshd /home/vscode/.ssh \
    && chmod 700 /home/vscode/.ssh \
    && chown -R vscode:vscode /home/vscode/.ssh \
    && ssh-keygen -A

# ~/.codex gets the same treatment. Codex authenticates with the ChatGPT/OpenAI
# state mounted from WSL2; without a volume
# that would silently vanish on every rebuild. postCreate.sh still re-seeds
# config.toml from the committed template on top of the volume, so the template
# stays the source of truth for configuration while auth persists.
RUN mkdir -p /home/vscode/.codex /workspaces \
    && chown -R vscode:vscode /home/vscode/.codex /workspaces \
    && chmod 0775 /workspaces

# Official GitHub MCP server. It runs directly as a stdio child of Codex,
# avoiding a nested-Docker secret mount. Version and per-architecture
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
COPY github-cli.sh /usr/local/bin/gh
COPY workspace-entrypoint.sh /usr/local/bin/teck-workspace-entrypoint
COPY runtime-doctor.sh /usr/local/bin/teck-runtime-doctor
COPY orca-ssh-entrypoint.sh /usr/local/bin/orca-docker-ssh-entrypoint
COPY orca-cli.sh /usr/local/bin/orca
COPY codex/orca-coordinator-stop.py /usr/local/lib/teck/codex/orca-coordinator-stop.py
COPY codex/hooks.json /etc/codex/hooks.json
RUN chmod 0755 /usr/local/bin/teck-github-mcp /usr/local/bin/gh \
      /usr/local/bin/teck-workspace-entrypoint \
      /usr/local/bin/teck-runtime-doctor /usr/local/bin/orca \
      /usr/local/bin/orca-docker-ssh-entrypoint \
      /usr/local/lib/teck/codex/orca-coordinator-stop.py \
    && chmod 0644 /etc/codex/hooks.json

#!/usr/bin/env bash
# One-time setup after the dev container is built. Runs as the `vscode` user
# with the repository as the working directory.
#
# NOTE: this script is intentionally tolerant of a partially-building baseline
# (the monorepo does not fully compile yet). Dependency-restore failures are
# reported but do NOT fail the container build, so the environment still comes
# up and an engineer can fix things from inside it.
set -uo pipefail

SETUP_CACHE_DIR="${TECK_SETUP_CACHE_DIR:-/workspaces/.teck-devcontainer-cache}"
mkdir -p "$SETUP_CACHE_DIR"

fingerprint_files() {
  local file
  for file in "$@"; do
    [ -f "$file" ] && sha256sum "$file"
  done | sha256sum | cut -d' ' -f1
}

dotnet_fingerprint="$(git ls-files -z -- '*.csproj' '*.fsproj' '*.sln' '*.slnx' \
  Directory.Build.props Directory.Packages.props global.json nuget.config \
  | sort -z | xargs -0 -r sha256sum 2>/dev/null | sha256sum | cut -d' ' -f1)"
if [ -f "$SETUP_CACHE_DIR/dotnet-restore" ] \
  && [ "$(<"$SETUP_CACHE_DIR/dotnet-restore")" = "$dotnet_fingerprint" ]; then
  echo "==> .NET dependencies unchanged; reusing restored packages"
else
  echo "==> Restoring .NET dependencies (dotnet restore)"
  if dotnet restore; then
    printf '%s' "$dotnet_fingerprint" > "$SETUP_CACHE_DIR/dotnet-restore"
  else
    echo "WARN: dotnet restore reported errors (continuing; baseline may not fully build yet)"
  fi
fi

js_fingerprint="$(fingerprint_files package.json bun.lock)"
if [ -d node_modules ] && [ -f "$SETUP_CACHE_DIR/bun-install" ] \
  && [ "$(<"$SETUP_CACHE_DIR/bun-install")" = "$js_fingerprint" ]; then
  echo "==> JavaScript dependencies unchanged; reusing node_modules"
else
  echo "==> Installing JS workspace dependencies (bun install)"
  if bun install --frozen-lockfile; then
    printf '%s' "$js_fingerprint" > "$SETUP_CACHE_DIR/bun-install"
  else
    echo "WARN: bun install reported errors (continuing)"
  fi
fi

echo "==> Ensuring a local HTTPS development certificate exists"
dotnet dev-certs https || echo "WARN: could not create HTTPS dev cert (continuing)"

echo "==> Configuring Oh My Codex"
# OMX is installed by the Node feature during the image build. Use its official
# user-scoped setup outside the repository so generated .omx state never dirties
# a worktree. Orca remains the lifecycle/worktree owner; OMX contributes only
# native Codex roles, skills, and hooks.
mkdir -p "$HOME/.codex" "$SETUP_CACHE_DIR/omx-setup"
cp .devcontainer/codex/config.toml "$HOME/.codex/config.toml" \
  || echo "WARN: could not seed Codex config (continuing)"
omx_version="$(omx --version 2>/dev/null || true)"
omx_stamp="$HOME/.codex/.teck-omx-version"
if [ -n "$omx_version" ] \
  && { [ ! -f "$omx_stamp" ] || [ "$(<"$omx_stamp")" != "$omx_version" ]; }; then
  if (cd "$SETUP_CACHE_DIR/omx-setup" \
      && omx setup --scope user --legacy --no-merge-agents </dev/null); then
    printf '%s' "$omx_version" > "$omx_stamp"
  else
    echo "WARN: OMX setup failed (continuing so the workspace remains repairable)"
  fi
elif [ -z "$omx_version" ]; then
  echo "WARN: 'omx' CLI is not on PATH (continuing)"
else
  echo "==> OMX $omx_version is already configured"
fi

echo "==> Configuring Git identity and GitHub CLI transport"
git config --local user.name 'CptPowerTurtle'
git config --local user.email 'jl@tecklab.dk'
git config --local commit.gpgsign false
git config --local credential.https://github.com.helper '!gh auth git-credential'

SECRET_ENV_MARKER='# Load read-only Teck runtime secrets without printing them.'
if ! grep -qxF "$SECRET_ENV_MARKER" "$HOME/.bashrc" 2>/dev/null; then
  printf '\n%s\n%s\n' "$SECRET_ENV_MARKER" \
    'for teck_env in /run/secrets/teck-mcp/mcp.env; do if [ -s "$teck_env" ]; then set -a; source "$teck_env"; set +a; fi; done; unset teck_env' \
    >> "$HOME/.bashrc"
fi

echo "==> postCreate complete"

#!/usr/bin/env bash
# One-time setup after the dev container is built. Runs as the `vscode` user
# with the repository as the working directory.
#
# NOTE: this script is intentionally tolerant of a partially-building baseline
# (the monorepo does not fully compile yet). Dependency-restore failures are
# reported but do NOT fail the container build, so the environment still comes
# up and an engineer can fix things from inside it.
set -uo pipefail

echo "==> Restoring .NET dependencies (dotnet restore)"
dotnet restore || echo "WARN: dotnet restore reported errors (continuing; baseline may not fully build yet)"

echo "==> Installing JS workspace dependencies (bun install)"
bun install --frozen-lockfile || echo "WARN: bun install reported errors (continuing)"

echo "==> Ensuring a local HTTPS development certificate exists"
dotnet dev-certs https || echo "WARN: could not create HTTPS dev cert (continuing)"

echo "==> Installing Claude Code plugins into the mounted ~/.claude volume"
# The committed .claude/settings.json declares these as enabled, but enabled
# plugins only auto-install behind an interactive trust prompt. Installing them
# explicitly here is headless and deterministic: `claude plugin install` just
# git-clones the (public) marketplaces into ~/.claude/plugins/, so it needs no
# auth and runs before first sign-in. Tolerant of failure so the container still
# comes up if GitHub is unreachable (the trust-prompt path remains as fallback).
if command -v claude >/dev/null 2>&1; then
  claude plugin marketplace add anthropics/claude-plugins-official || echo "WARN: marketplace add (official) failed (continuing)"
  claude plugin marketplace add jarrodwatts/claude-hud || echo "WARN: marketplace add (claude-hud) failed (continuing)"
  for p in superpowers microsoft-docs typescript-lsp security-guidance playwright frontend-design csharp-lsp github; do
    claude plugin install "$p@claude-plugins-official" || echo "WARN: install $p failed (continuing)"
  done
  claude plugin install claude-hud@claude-hud || echo "WARN: install claude-hud failed (continuing)"

  echo "==> Seeding claude-hud display config"
  mkdir -p "$HOME/.claude/plugins/claude-hud"
  cp .devcontainer/claude-hud-config.json "$HOME/.claude/plugins/claude-hud/config.json" || echo "WARN: could not seed claude-hud config (continuing)"
else
  echo "WARN: 'claude' CLI not on PATH; skipping plugin install (continuing)"
fi

echo "==> Installing low-prompt 'claude' alias in ~/.bashrc"
ALIAS_LINE="alias claude='claude --dangerously-skip-permissions'"
if ! grep -qxF "$ALIAS_LINE" "$HOME/.bashrc" 2>/dev/null; then
  printf '\n# Convenience-first: run Claude Code without permission prompts inside the isolated container\n%s\n' "$ALIAS_LINE" >> "$HOME/.bashrc"
fi

echo "==> postCreate complete"

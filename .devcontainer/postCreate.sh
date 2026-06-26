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

# --- Persisted, cross-container credentials --------------------------------
# `~/.config/gh` and `~/.ssh` are backed by FIXED-name Docker volumes
# (shared-gh-config / shared-ssh) declared in devcontainer.json, so anything
# stored there survives rebuilds AND is shared by every container that mounts
# the same volume names. Fresh volumes are created root-owned, so reclaim them.
echo "==> Fixing ownership/permissions on persisted credential volumes"
if command -v sudo >/dev/null 2>&1; then
  sudo chown -R "$(id -u):$(id -g)" "$HOME/.ssh" "$HOME/.config/gh" 2>/dev/null || true
fi
chmod 700 "$HOME/.ssh" 2>/dev/null || true

# Let git reuse the gh token for HTTPS (push/pull, gh pr, etc.). The login
# itself lives in the shared volume, so you only run `gh auth login` once.
echo "==> Wiring gh as the git credential helper"
if command -v gh >/dev/null 2>&1 && gh auth status >/dev/null 2>&1; then
  gh auth setup-git || echo "WARN: gh auth setup-git failed (continuing)"
else
  echo "    gh is not authenticated yet — run 'gh auth login' once; it persists in the shared volume"
fi

# Enable SSH-based commit signing using the agent VS Code forwards from your
# host, then register the PUBLIC key with GitHub as a signing key so commits
# show the green "Verified" badge. No private key ever enters the container.
# Any step we cannot finish non-interactively is written to a hint file that
# every new shell prints (see the reminder block below) until it is resolved.
echo "==> Configuring SSH commit signing"
HINT_FILE="$HOME/.devcontainer-signing-hint"
rm -f "$HINT_FILE"
SIGNING_KEY="$(ssh-add -L 2>/dev/null | head -n1 || true)"
if [ -n "$SIGNING_KEY" ]; then
  # Turn signing on only if it isn't already configured (respect inherited GPG).
  if [ -z "$(git config --global --get commit.gpgsign || true)" ]; then
    git config --global gpg.format ssh
    git config --global user.signingkey "$SIGNING_KEY"
    git config --global commit.gpgsign true
    git config --global tag.gpgsign true
    echo "    SSH commit signing enabled using the forwarded agent key"
  fi

  # Register the public key with GitHub as a *signing* key (idempotent). This
  # is what turns commits "Verified". Needs the admin:ssh_signing_key scope.
  if command -v gh >/dev/null 2>&1 && gh auth status >/dev/null 2>&1; then
    KEY_BODY="$(printf '%s' "$SIGNING_KEY" | awk '{print $2}')"
    if gh ssh-key list 2>/dev/null | grep -qF "$KEY_BODY"; then
      echo "    Signing key already registered on GitHub — commits will show Verified"
    else
      TMP_PUB="$(mktemp)"
      printf '%s\n' "$SIGNING_KEY" > "$TMP_PUB"
      if gh ssh-key add "$TMP_PUB" --type signing --title "devcontainer signing key" 2>/dev/null; then
        echo "    Registered SSH signing key on GitHub — commits will show Verified"
      else
        cat > "$HINT_FILE" <<'HINT'
[devcontainer] Commits sign locally but are NOT yet "Verified" on GitHub.
Grant the one-time scope and register the key:
    gh auth refresh -h github.com -s admin:ssh_signing_key
    bash .devcontainer/postCreate.sh
HINT
      fi
      rm -f "$TMP_PUB"
    fi
  else
    cat > "$HINT_FILE" <<'HINT'
[devcontainer] Commits will sign and become "Verified" once gh is authenticated:
    gh auth login
    bash .devcontainer/postCreate.sh
HINT
  fi
else
  if [ -z "$(git config --global --get commit.gpgsign || true)" ]; then
    echo "    No SSH key in the forwarded agent and no signing configured — commits will be unsigned"
  fi
fi

# Surface the pending-setup reminder in every new shell until it is resolved
# (the hint file is removed above as soon as registration succeeds).
REMIND_LINE='[ -f "$HOME/.devcontainer-signing-hint" ] && cat "$HOME/.devcontainer-signing-hint"'
if ! grep -qxF "$REMIND_LINE" "$HOME/.bashrc" 2>/dev/null; then
  printf '\n# Remind about pending commit-signing setup until it is done\n%s\n' "$REMIND_LINE" >> "$HOME/.bashrc"
fi
if [ -f "$HINT_FILE" ]; then
  echo ""
  cat "$HINT_FILE"
fi

echo "==> Installing low-prompt 'claude' alias in ~/.bashrc"
ALIAS_LINE="alias claude='claude --dangerously-skip-permissions'"
if ! grep -qxF "$ALIAS_LINE" "$HOME/.bashrc" 2>/dev/null; then
  printf '\n# Convenience-first: run Claude Code without permission prompts inside the isolated container\n%s\n' "$ALIAS_LINE" >> "$HOME/.bashrc"
fi

echo "==> postCreate complete"

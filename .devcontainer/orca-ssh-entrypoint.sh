#!/usr/bin/env bash
set -euo pipefail

: "${ORCA_SSH_PUBLIC_KEY:?ORCA_SSH_PUBLIC_KEY is required}"

# Orca creates feature worktrees as siblings of the primary checkout. The
# workspace mount itself is writable by vscode, but its image-owned parent is
# root-owned unless the remote-runtime entrypoint prepares it explicitly.
install -d -m 0775 -o vscode -g vscode /workspaces

for env_file in /run/secrets/teck-mcp/mcp.env; do
  if [ -s "$env_file" ]; then
    set -a
    # shellcheck disable=SC1090
    source "$env_file"
    set +a
  fi
done

install -d -m 0700 -o vscode -g vscode /home/vscode/.ssh
printf '%s\n' "$ORCA_SSH_PUBLIC_KEY" > /home/vscode/.ssh/authorized_keys
chown vscode:vscode /home/vscode/.ssh/authorized_keys
chmod 0600 /home/vscode/.ssh/authorized_keys
test -s /etc/ssh/ssh_host_ed25519_key || ssh-keygen -A
exec /usr/sbin/sshd -D -e

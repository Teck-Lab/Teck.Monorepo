#!/usr/bin/env bash
set -euo pipefail

: "${ORCA_SSH_PUBLIC_KEY:?ORCA_SSH_PUBLIC_KEY is required}"
install -d -m 0700 -o vscode -g vscode /home/vscode/.ssh
printf '%s\n' "$ORCA_SSH_PUBLIC_KEY" > /home/vscode/.ssh/authorized_keys
chown vscode:vscode /home/vscode/.ssh/authorized_keys
chmod 0600 /home/vscode/.ssh/authorized_keys

# Host keys are generated while building the image so every disposable
# container presents the same key. This guard only protects derived images.
test -s /etc/ssh/ssh_host_ed25519_key || ssh-keygen -A
exec /usr/sbin/sshd -D -e

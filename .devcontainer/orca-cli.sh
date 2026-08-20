#!/bin/sh
set -eu

# Orca installs the version-matched relay CLI after it connects to this
# workspace. Agent processes do not consistently inherit the relay bin
# directory on PATH, so expose a stable container command without copying or
# pinning Orca itself.
relay_cli="${HOME}/.orca-relay/bin/orca"

if [ ! -x "${relay_cli}" ]; then
  echo "Orca managed CLI is not available at ${relay_cli}; open this workspace through Orca first." >&2
  exit 127
fi

exec "${relay_cli}" "$@"

#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
usage: teck-omo-worker --worktree PATH --parent-issue N --issue N --slug SLUG
                       [--mode planned|autonomous|quick|spike]
                       [--dry-run]
EOF
}

worktree=""
parent_issue=""
issue=""
slug=""
mode="planned"
dry_run=false

while (($#)); do
  case "$1" in
    --worktree) worktree="${2:-}"; shift 2 ;;
    --parent-issue) parent_issue="${2:-}"; shift 2 ;;
    --issue) issue="${2:-}"; shift 2 ;;
    --slug) slug="${2:-}"; shift 2 ;;
    --mode) mode="${2:-}"; shift 2 ;;
    --dry-run) dry_run=true; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "unknown argument: $1" >&2; usage >&2; exit 2 ;;
  esac
done

[[ "$parent_issue" =~ ^[1-9][0-9]*$ ]] || { echo "--parent-issue must be a positive integer" >&2; exit 2; }
[[ "$issue" =~ ^[1-9][0-9]*$ ]] || { echo "--issue must be a positive integer" >&2; exit 2; }
case "$mode" in
  planned|quick) primary_agent="prometheus" ;;
  autonomous|spike) primary_agent="hephaestus" ;;
  *) echo "unsupported --mode: $mode" >&2; exit 2 ;;
esac
config_dir="$HOME/.config/opencode"

test -n "$worktree" || { echo "--worktree is required" >&2; exit 2; }
test -n "$slug" || { echo "--slug is required" >&2; exit 2; }
worktree="$(realpath "$worktree")"
test -d "$worktree" || { echo "worktree does not exist: $worktree" >&2; exit 1; }
git_root="$(git -C "$worktree" rev-parse --show-toplevel)"
test "$(realpath "$git_root")" = "$worktree" || {
  echo "assigned path is not the root of its Git worktree: $worktree" >&2
  exit 1
}

# Bun may colorize numeric console output when the launcher runs inside Orca's
# PTY. ANSI bytes make the numeric guard reject an otherwise valid port.
port="$(NO_COLOR=1 bun -e 'const s=Bun.listen({hostname:"127.0.0.1",port:0,socket:{data(){}}}); console.log(s.port); s.stop();')"
[[ "$port" =~ ^[1-9][0-9]*$ ]] || { echo "could not allocate an OpenCode port" >&2; exit 1; }

if $dry_run; then
  printf '{"agent":"%s","mode":"%s","worktree":"%s","port":%s,"configDir":"%s"}\n' \
    "$primary_agent" "$mode" "$worktree" "$port" "$config_dir"
  exit 0
fi

test -s "$config_dir/opencode.json" || { echo "OpenCode profile is not seeded: $config_dir/opencode.json" >&2; exit 1; }
command -v opencode >/dev/null || { echo "opencode is not installed" >&2; exit 1; }

cd "$worktree"
exec env \
  OPENCODE_CONFIG_DIR="$config_dir" \
  OPENCODE_PORT="$port" \
  OPENCODE_EXPERIMENTAL_BACKGROUND_SUBAGENTS=true \
  OMO_DISABLE_POSTHOG=1 \
  OMO_SEND_ANONYMOUS_TELEMETRY=0 \
  opencode --port "$port" --agent "$primary_agent"

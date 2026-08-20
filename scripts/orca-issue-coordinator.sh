#!/usr/bin/env bash
set -euo pipefail

issue_url="${1:-}"
if [[ ! "$issue_url" =~ ^https://github\.com/Teck-Lab/Teck\.Monorepo/issues/[1-9][0-9]*$ ]]; then
  echo "expected a Teck.Monorepo GitHub issue URL, got: ${issue_url:-<empty>}" >&2
  exit 2
fi

worktree="${ORCA_WORKTREE_PATH:-$PWD}"
worktree="$(realpath "$worktree")"
git_root="$(git -C "$worktree" rev-parse --show-toplevel)"
if [[ "$(realpath "$git_root")" != "$worktree" ]]; then
  echo "Orca workspace is not the root of its Git worktree: $worktree" >&2
  exit 1
fi

if [[ -n "${ORCA_CLI_COMMAND:-}" ]]; then
  read -r -a orca_cli <<<"$ORCA_CLI_COMMAND"
else
  orca_cli=(orca)
fi
command -v "${orca_cli[0]}" >/dev/null || {
  echo "Orca CLI is not available in the workspace environment" >&2
  exit 1
}

prompt="You are the Orca coordinator for ${issue_url}.

Load and follow the repository's teck-feature-flow skill, including its referenced workflow. Also load the Orca orchestration skill and its version-matched live guide before running orchestration commands.

Treat the linked GitHub issue as the parent feature and execute the complete coordinator -> OMO planner -> coordinator-reviewed Task DAG -> OMO child worker -> coordinator integration flow. Do not implement the feature directly in this coordinator worktree, and do not substitute untracked subagents for Orca Dispatches."

cd "$worktree"

# Orca deliberately restores focus to the original agent pane after creating
# this issue-command split. With no --terminal selector, these commands target
# that focused agent rather than this short-lived helper shell.
"${orca_cli[@]}" terminal wait --for tui-idle --timeout-ms 120000 --json >/dev/null
"${orca_cli[@]}" terminal send --text "$prompt" --enter --json >/dev/null

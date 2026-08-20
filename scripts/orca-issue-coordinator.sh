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
command -v jq >/dev/null || {
  echo "jq is required to resolve the existing OpenCode terminal" >&2
  exit 1
}

prompt="You are the Orca coordinator for ${issue_url}.

Load and follow the repository's teck-feature-flow skill, including its referenced workflow. Also load the Orca orchestration skill and its version-matched live guide before running orchestration commands.

Treat the linked GitHub issue as the parent feature and execute the complete coordinator -> OMO planner -> coordinator-reviewed Task DAG -> OMO child worker -> coordinator integration flow. Do not implement the feature directly in this coordinator worktree, and do not substitute untracked subagents for Orca Dispatches."

cd "$worktree"

# The issue-command shell is the active leaf by the time this command runs.
# Resolve other panes in the same worktree, then let Orca's TUI readiness probe
# distinguish the selected agent from setup or ordinary shell panes.
terminal_list="$(
  "${orca_cli[@]}" terminal list \
    --worktree "path:$worktree" \
    --include-visual-layouts \
    --json
)"
mapfile -t candidates < <(
  jq -r --arg worktree "$worktree" '
    [
      .result.visualLayouts[]?
      | select(.worktreePath == $worktree)
      | ..
      | objects
      | .activeLeafId?
      | select(. != null)
    ] as $activeLeaves
    | .result.terminals[]?
    | select(
        .worktreePath == $worktree
        and .connected == true
        and .writable == true
      )
    | .leafId as $leaf
    | select(($activeLeaves | index($leaf)) == null)
    | .handle
  ' <<<"$terminal_list"
)

agent_handle=""
for candidate in "${candidates[@]}"; do
  if "${orca_cli[@]}" terminal wait \
    --terminal "$candidate" \
    --for tui-idle \
    --timeout-ms 120000 \
    --json >/dev/null; then
    agent_handle="$candidate"
    break
  fi
done

if [[ -z "$agent_handle" ]]; then
  echo "could not resolve an idle agent terminal outside the issue-command split" >&2
  exit 1
fi

"${orca_cli[@]}" terminal send \
  --terminal "$agent_handle" \
  --text "$prompt" \
  --enter \
  --json >/dev/null

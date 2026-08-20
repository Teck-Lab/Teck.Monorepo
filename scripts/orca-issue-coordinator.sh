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
command -v opencode >/dev/null || {
  echo "OpenCode is not installed in the workspace environment" >&2
  exit 1
}
config_dir="$HOME/.config/opencode"
[[ -s "$config_dir/opencode.json" ]] || {
  echo "OpenCode profile is not seeded: $config_dir/opencode.json" >&2
  exit 1
}

prompt="You are the Orca coordinator for ${issue_url}.

Load and follow the repository's teck-feature-flow skill, including its referenced workflow. Also load the Orca orchestration skill and its version-matched live guide before running orchestration commands.

Treat the linked GitHub issue as the parent feature and execute the complete coordinator -> OMO planner -> coordinator-reviewed Task DAG -> OMO child worker -> coordinator integration flow. Do not implement the feature directly in this coordinator worktree, and do not substitute untracked subagents for Orca Dispatches."

cd "$worktree"

# Orca launches the selected agent and this issue-command shell in separate
# panes. Relay input delivery to the selected OpenCode pane is not reliable, so
# identify that empty agent pane, close it, and replace this shell process with
# the coordinator TUI using OpenCode's native initial-prompt option.
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

if (( ${#candidates[@]} != 1 )); then
  echo "expected exactly one sibling agent terminal, found ${#candidates[@]}" >&2
  jq -r --arg worktree "$worktree" '
    .result.terminals[]?
    | select(.worktreePath == $worktree)
    | "terminal=\(.handle) leaf=\(.leafId) title=\(.title) connected=\(.connected) writable=\(.writable)"
  ' <<<"$terminal_list" >&2
  exit 1
fi

agent_handle="${candidates[0]}"
"${orca_cli[@]}" terminal close --terminal "$agent_handle" --json >/dev/null

exec env \
  OPENCODE_CONFIG_DIR="$config_dir" \
  OPENCODE_EXPERIMENTAL_BACKGROUND_SUBAGENTS=true \
  OMO_DISABLE_POSTHOG=1 \
  OMO_SEND_ANONYMOUS_TELEMETRY=0 \
  opencode --agent sisyphus --prompt "$prompt"

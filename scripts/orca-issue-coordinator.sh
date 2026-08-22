#!/usr/bin/env bash
set -euo pipefail

issue_url="${1:-}"
if [[ ! "$issue_url" =~ ^https://github\.com/Teck-Lab/Teck\.Monorepo/issues/[1-9][0-9]*$ ]]; then
  echo "expected a Teck.Monorepo GitHub issue URL, got: ${issue_url:-<empty>}" >&2
  exit 2
fi

worktree="$(realpath "${ORCA_WORKTREE_PATH:-$PWD}")"
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
  echo 'Orca CLI is not available in the workspace environment.' >&2
  exit 1
}
command -v jq >/dev/null || {
  echo 'jq is required to resolve the original agent terminal.' >&2
  exit 1
}
command -v codex >/dev/null || {
  echo 'Codex is not installed in the workspace environment.' >&2
  exit 1
}

cd "$worktree"
terminal_list="$(
  "${orca_cli[@]}" terminal list \
    --worktree "path:$worktree" \
    --include-visual-layouts \
    --json
)"
mapfile -t candidates < <(
  jq -r --arg worktree "$worktree" --arg self "${ORCA_TERMINAL_HANDLE:-}" '
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
        and .handle != $self
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
  echo 'could not resolve the original Codex terminal for issue handoff.' >&2
  exit 1
fi

"${orca_cli[@]}" terminal close --terminal "$agent_handle" --json >/dev/null
exec codex --dangerously-bypass-approvals-and-sandbox "$issue_url"

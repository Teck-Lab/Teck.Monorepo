#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
coordinator="$repo_root/scripts/orca-issue-coordinator.sh"
fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT
mkdir -p "$fixture/bin"

cat >"$fixture/bin/orca-test" <<'EOF'
#!/usr/bin/env bash
printf '%s\n' "$*" >>"$ORCA_TEST_CLI_LOG"
case "$*" in
  'terminal list '*)
    jq -cn --arg root "$ORCA_WORKTREE_PATH" '{result:{visualLayouts:[{worktreePath:$root,activeLeafId:"self-leaf"}],terminals:[{handle:"self",leafId:"self-leaf",worktreePath:$root,connected:true,writable:true},{handle:"agent",leafId:"agent-leaf",worktreePath:$root,connected:true,writable:true}]}}'
    ;;
  'terminal wait '*) printf '{"ok":true}\n' ;;
  'terminal close '*) printf '{"ok":true}\n' ;;
  *) exit 1 ;;
esac
EOF
cat >"$fixture/bin/codex" <<'EOF'
#!/usr/bin/env bash
printf '%s\n' "$*" >"$ORCA_TEST_CODEX_LOG"
EOF
chmod +x "$fixture/bin/orca-test" "$fixture/bin/codex"

export PATH="$fixture/bin:$PATH"
export ORCA_CLI_COMMAND=orca-test
export ORCA_WORKTREE_PATH="$repo_root"
export ORCA_TERMINAL_HANDLE=self
export ORCA_TEST_CLI_LOG="$fixture/orca.log"
export ORCA_TEST_CODEX_LOG="$fixture/codex.log"
issue_url='https://github.com/Teck-Lab/Teck.Monorepo/issues/42'

bash "$coordinator" "$issue_url"

grep -q '^terminal wait --terminal agent --for tui-idle --timeout-ms 120000 --json$' "$ORCA_TEST_CLI_LOG"
grep -q '^terminal close --terminal agent --json$' "$ORCA_TEST_CLI_LOG"
grep -q -- "^--dangerously-bypass-approvals-and-sandbox $issue_url$" "$ORCA_TEST_CODEX_LOG"
if bash "$coordinator" 'https://github.com/other/repo/issues/42' >/dev/null 2>&1; then
  echo 'coordinator accepted an issue outside Teck.Monorepo' >&2
  exit 1
fi

echo 'Orca issue coordinator contract passed.'

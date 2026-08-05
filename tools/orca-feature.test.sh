#!/usr/bin/env bash
set -euo pipefail

tool="$(cd "$(dirname "$0")" && pwd)/orca-feature"
fixture="$(mktemp -d)"
trap 'tmux kill-session -t =teck-120-121-tax-system >/dev/null 2>&1 || true; rm -rf "$fixture"' EXIT

git init --bare "$fixture/origin.git" >/dev/null
git clone "$fixture/origin.git" "$fixture/repo" >/dev/null 2>&1
git -C "$fixture/repo" config user.name 'Test Agent'
git -C "$fixture/repo" config user.email 'agent@example.test'
git -C "$fixture/repo" config commit.gpgsign false
git -C "$fixture/repo" config orca.feature.requireSignatures false
git -C "$fixture/repo" switch -c main >/dev/null
printf 'root\n' > "$fixture/repo/README.md"
git -C "$fixture/repo" add README.md
git -C "$fixture/repo" commit -m 'chore: initialize fixture' >/dev/null
git -C "$fixture/repo" push -u origin main >/dev/null
git -C "$fixture/origin.git" symbolic-ref HEAD refs/heads/main
git -C "$fixture/repo" remote set-head origin --auto >/dev/null

cd "$fixture/repo"
"$tool" init --issue 120 --slug billing-overhaul --title 'Billing overhaul' --create-branch
"$tool" add --issue 121 --title 'Tax system' --kind feature
"$tool" add --issue 122 --title 'Plan review defect' --kind plan-defect --mode autonomous --depends-on 121

tmux new-session -d -s teck-120-121-tax-system
"$tool" stop --issue 121 >/dev/null
if tmux has-session -t =teck-120-121-tax-system 2>/dev/null; then
  echo 'worker tmux session was not stopped' >&2
  exit 1
fi

tax_path="$fixture/.orca-worktrees/120/121-tax-system"
printf 'tax\n' > "$tax_path/tax.txt"
git -C "$tax_path" add tax.txt
git -C "$tax_path" commit -m 'feat(billing): add tax system' >/dev/null

blocked="$("$tool" dispatch-info --issue 122)"
bun -e 'const d=JSON.parse(await Bun.stdin.text()); if (d.ready !== false || JSON.stringify(d.blockedBy) !== "[121]" || d.executionMode !== "autonomous" || d.primaryAgent !== "hephaestus" || !d.terminalCommand.includes("teck-omo-worker")) process.exit(1)' <<<"$blocked"

"$tool" set-status --issue 121 --status completed
git config orca.feature.requireSignatures true
if "$tool" integrate --issue 121 >"$fixture/unsigned.out" 2>"$fixture/unsigned.err"; then
  echo 'FAIL: unsigned worker commit was integrated' >&2
  exit 1
fi
grep -q 'not signed and verifiable' "$fixture/unsigned.err"
git config orca.feature.requireSignatures false
"$tool" integrate --issue 121
ready="$("$tool" dispatch-info --issue 122)"
bun -e 'const d=JSON.parse(await Bun.stdin.text()); if (d.ready !== false || d.needsSync !== true || d.blockedBy.length) process.exit(1)' <<<"$ready"
"$tool" sync --issue 122
ready="$("$tool" dispatch-info --issue 122)"
bun -e 'const d=JSON.parse(await Bun.stdin.text()); if (d.ready !== true || d.needsSync !== false) process.exit(1)' <<<"$ready"

defect_path="$fixture/.orca-worktrees/120/122-plan-review-defect"
printf 'defect\n' > "$defect_path/defect.txt"
git -C "$defect_path" add defect.txt
git -C "$defect_path" commit -m 'fix(billing): resolve plan review defect' >/dev/null
"$tool" set-status --issue 122 --status completed
"$tool" integrate --issue 122

status="$("$tool" status --json)"
bun -e 'const d=JSON.parse(await Bun.stdin.text()); if (!d.parentClean || !d.worktrees.every((x) => x.merged)) process.exit(1)' <<<"$status"
pr="$("$tool" pr-info)"
bun -e 'const d=JSON.parse(await Bun.stdin.text()); if (!d.ready || JSON.stringify(d.integratedSubIssues) !== "[121,122]") process.exit(1)' <<<"$pr"

"$tool" remove --issue 121
"$tool" remove --issue 122
[ ! -e "$tax_path" ] && [ ! -e "$defect_path" ]
echo 'PASS: internal feature worktree lifecycle'

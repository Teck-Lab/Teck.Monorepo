#!/usr/bin/env bash
set -euo pipefail

tool="$(cd "$(dirname "$0")" && pwd)/orca-feature"
fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT

git init --bare "$fixture/origin.git" >/dev/null
git clone "$fixture/origin.git" "$fixture/repo" >/dev/null 2>&1
git -C "$fixture/repo" config user.name 'Test Agent'
git -C "$fixture/repo" config user.email 'agent@example.test'
git -C "$fixture/repo" config commit.gpgsign false
git -C "$fixture/repo" switch -c main >/dev/null
printf 'root\n' > "$fixture/repo/README.md"
git -C "$fixture/repo" add README.md
git -C "$fixture/repo" commit -m 'chore: initialize fixture' >/dev/null
git -C "$fixture/repo" push -u origin main >/dev/null
git -C "$fixture/origin.git" symbolic-ref HEAD refs/heads/main
git -C "$fixture/repo" remote set-head origin --auto >/dev/null

cat > "$fixture/promote" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
message=''
branch=''
while [ "$#" -gt 0 ]; do
  case "$1" in
    --message) message="$2" ;;
    --branch) branch="$2" ;;
  esac
  shift 2
done
git -c commit.gpgsign=false commit -m "$message" >/dev/null
sha="$(git rev-parse HEAD)"
git push origin "HEAD:refs/heads/$branch" >/dev/null
printf '{"sha":"%s","verified":true}\n' "$sha"
EOF
chmod +x "$fixture/promote"
export TECK_GITHUB_PROMOTE_COMMAND="$fixture/promote"

cd "$fixture/repo"
"$tool" init --issue 120 --slug billing-overhaul --title 'Billing overhaul' --create-branch
tax_path="$fixture/issue-121-tax-system"
defect_path="$fixture/issue-122-plan-review-defect"
git worktree add -b subfeature/120/121-tax-system "$tax_path" feature/120-billing-overhaul >/dev/null
"$tool" register --issue 121 --title 'Tax system' --kind feature \
  --path "$tax_path" --branch subfeature/120/121-tax-system \
  --worktree-id "fixture::$tax_path"
git worktree add -b subfeature/120/122-plan-review-defect "$defect_path" feature/120-billing-overhaul >/dev/null
"$tool" register --issue 122 --title 'Plan review defect' --kind plan-defect \
  --mode autonomous --depends-on 121 --path "$defect_path" \
  --branch subfeature/120/122-plan-review-defect --worktree-id "fixture::$defect_path"

printf 'tax\n' > "$tax_path/tax.txt"
git -C "$tax_path" add tax.txt
git -C "$tax_path" commit -m 'feat(billing): add tax system' >/dev/null

blocked="$("$tool" dispatch-info --issue 122)"
bun -e 'const d=JSON.parse(await Bun.stdin.text()); if (d.ready !== false || JSON.stringify(d.blockedBy) !== "[121]" || d.executionMode !== "autonomous" || d.primaryAgent !== "hephaestus" || !d.terminalCommand.includes("teck-omo-worker")) process.exit(1)' <<<"$blocked"

if "$tool" dispatch-info --issue 122 --harness slim >/dev/null 2>&1; then
  echo "dispatch-info accepted the retired --harness option" >&2
  exit 1
fi

"$tool" set-status --issue 121 --status completed
"$tool" integrate --issue 121
ready="$("$tool" dispatch-info --issue 122)"
bun -e 'const d=JSON.parse(await Bun.stdin.text()); if (d.ready !== false || d.needsSync !== true || d.blockedBy.length) process.exit(1)' <<<"$ready"
"$tool" sync --issue 122
ready="$("$tool" dispatch-info --issue 122)"
bun -e 'const d=JSON.parse(await Bun.stdin.text()); if (d.ready !== true || d.needsSync !== false) process.exit(1)' <<<"$ready"

printf 'defect\n' > "$defect_path/defect.txt"
git -C "$defect_path" add defect.txt
git -C "$defect_path" commit -m 'fix(billing): resolve plan review defect' >/dev/null
"$tool" set-status --issue 122 --status completed
"$tool" integrate --issue 122

status="$("$tool" status --json)"
bun -e 'const d=JSON.parse(await Bun.stdin.text()); if (!d.parentClean || !d.worktrees.every((x) => x.merged)) process.exit(1)' <<<"$status"
pr="$("$tool" pr-info)"
bun -e 'const d=JSON.parse(await Bun.stdin.text()); if (!d.ready || JSON.stringify(d.integratedSubIssues) !== "[121,122]") process.exit(1)' <<<"$pr"

git worktree remove "$tax_path"
git worktree remove "$defect_path"
"$tool" remove --issue 121
"$tool" remove --issue 122
[ ! -e "$tax_path" ] && [ ! -e "$defect_path" ]
echo 'PASS: native Orca child registration and integration lifecycle'

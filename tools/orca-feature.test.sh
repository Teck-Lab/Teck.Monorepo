#!/usr/bin/env bash
set -euo pipefail

test -f .codex/hooks.json
test -x tools/orca-coordinator-hook
test -x tools/orca-coordinator-hook.test.sh
jq -e '.hooks.Stop and .hooks.PostToolUse' .codex/hooks.json >/dev/null
tools/orca-coordinator-hook.test.sh

grep -Fxq '@AGENTS.md' CLAUDE.md
test -x tools/sync-agent-skills
tools/sync-agent-skills --check

# agentskill.sh packages retain their upstream package metadata. Codex's
# narrower quick validator is applied only to Teck-authored skills.
grep -Fq 'compatibility: Requires Node.js 18+ (for npx)' .agents/skills/agentskill-sh-learn/SKILL.md
grep -Fq '  - references/**' .agents/skills/agentskill-sh-learn/SKILL.md
grep -Fq '  - references/**' .agents/skills/agentskill-sh-review-skill/SKILL.md

# Matt Pocock discovery packages remain complete upstream copies. Teck owns a
# thin publication boundary rather than modifying their behavior in place.
for skill in grill-with-docs grilling domain-modeling wayfinder research prototype handoff; do
  test -f ".agents/skills/$skill/SKILL.md"
  test -f ".agents/skills/$skill/agents/openai.yaml"
done
test -f .agents/skills/domain-modeling/CONTEXT-FORMAT.md
test -f .agents/skills/domain-modeling/ADR-FORMAT.md
test -f .agents/skills/prototype/LOGIC.md
test -f .agents/skills/prototype/UI.md

discovery_skill=.agents/skills/teck-feature-request/SKILL.md
discovery_format=.agents/skills/teck-feature-request/references/feature-request-format.md
for contract in \
  'only after explicit human approval of the exact draft' \
  'create exactly one GitHub parent issue' \
  'never use it to transfer an active Orca coordinator or worker' \
  'even when they do not know or mention any skill name' \
  'do not ask the user which workflow or skill to use' \
  'without requiring a second command or a skill name' \
  'Never respond with a menu of internal skill names'; do
  grep -Fq "$contract" "$discovery_skill"
done
for contract in \
  '## Outcome' \
  '## Scope' \
  '## Out of scope' \
  '## Acceptance criteria' \
  '## Ready for Orca' \
  'never copied into the executable issue DAG'; do
  grep -Fq "$contract" "$discovery_format"
done
grep -Fq 'Wayfinder maps and children are decision records' AGENTS.md
grep -Fq 'never Orca' AGENTS.md
grep -Fq 'must not plan, decompose,' AGENTS.md
grep -Fq 'dispatch, branch, code, or create a PR.' AGENTS.md
for contract in \
  'asks to brainstorm, shape,' \
  '`teck-feature-request` immediately' \
  'Do not require the user to know, select, or' \
  'never tell the user to issue another command'; do
  grep -Fq "$contract" AGENTS.md
done

tool="$(cd "$(dirname "$0")" && pwd)/orca-feature"
workflow="$(cd "$(dirname "$0")/.." && pwd)/.agents/skills/teck-feature-flow/references/workflow.md"
state_machine="$(cd "$(dirname "$0")/.." && pwd)/.agents/skills/teck-feature-flow/references/state-machine.md"
agent_instructions="$(cd "$(dirname "$0")/.." && pwd)/AGENTS.md"
planner_instructions="$(cd "$(dirname "$0")/.." && pwd)/.agents/skills/teck-feature-planner/SKILL.md"
executor_instructions="$(cd "$(dirname "$0")/.." && pwd)/.agents/skills/teck-feature-executor/SKILL.md"
convergence="$(cd "$(dirname "$0")/.." && pwd)/.agents/skills/teck-feature-flow/references/review-convergence.md"
fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT

for contract in \
  'Starting a worker begins supervision' \
  'worker_done.*automatically marks that' \
  'task-update --status completed' \
  'foreground rolling wait' \
  'timeout or.*count:0.*checkpoint' \
  'ordinary `orca orchestration check --json` to recover mail' \
  'check --ack <delivery-id> --wait' \
  'Do not end the coordinator turn while active workers remain' \
  'Orca will re-engage the coordinator' \
  'still-running `check --wait` process' \
  'stablyai/orca#11787' \
  '#10663 reports typed waits missing queued mail' \
  '#9228 tracks durable coordinator wake/resume' \
  '#15185 condition where ready work can fail to wake' \
  'dispatch every newly eligible Task' \
  'external-state clause is subordinate to blocker-first progression' \
  'Required blockers outside the current parent' \
  'Partial work is a recovery input' \
  'ownership resolution takes precedence over every generic external-wait' \
  'completed-but-unreconciled Dispatch' \
  'open actionable GitHub sub-issue' \
  'dispatch a fresh independent plan reviewer'; do
  grep -Eq "$contract" "$workflow" || {
    echo "Missing coordinator completion-loop contract: $contract" >&2
    exit 1
  }
done

for contract in \
  'claude-opus-5`/high as the parent coordinator' \
  'gpt-5.6-sol`/high only when Claude' \
  'Never allow both coordinators to remain live'; do
  grep -Fq "$contract" "$agent_instructions" || {
    echo "Missing coordinator model fallback contract: $contract" >&2
    exit 1
  }
done

for contract in \
  'Luna/xhigh' \
  'Terra/high' \
  'Sol/high' \
  'parallel child-worktree Task'; do
  grep -Fq "$contract" "$planner_instructions" || {
    echo "Missing planner execution hierarchy contract: $contract" >&2
    exit 1
  }
done
grep -Fq 'A Terra consolidator inspects every member commit' "$executor_instructions"
grep -Fq 'Never create a separate code-review or QA gate per child' "$convergence"

for contract in \
  'canonical readable structure' \
  'body-file input' \
  'Never construct issue Markdown with literal `\\n` escapes' \
  'malformed or unreadable issue is an unreconciled mutation' \
  'First read its complete title, body, comments, relationships'; do
  grep -Fq "$contract" "$workflow" || {
    echo "Missing readable GitHub issue contract: $contract" >&2
    exit 1
  }
done

for contract in \
  'Executable frontier and claims' \
  'full title, body, labels, comments' \
  'matching current live Dispatch is not a' \
  'Immediately before accepting, integrating, or closing' \
  'final response between frontier transitions.' \
  'use `[Issue title](URL)`'; do
  grep -Fq "$contract" "$workflow" || {
    echo "Missing deterministic frontier/claim contract: $contract" >&2
    exit 1
  }
done

for contract in \
  'foreground rolling `check --wait` loop' \
  'still-running worker is never permission to return a final response' \
  'Assignment of a parent issue gives its coordinator outcome ownership' \
  'ownership are never external-state stopping conditions'; do
  grep -Fq "$contract" "$agent_instructions" || {
    echo "Missing parent coordinator startup contract: $contract" >&2
    exit 1
  }
done

for forbidden_contract in \
  'A coordinator Codex turn may end between events while active workers remain' \
  'let the native Orca mail-pointer mechanism re-engage'; do
  if grep -Fq "$forbidden_contract" "$workflow" "$state_machine"; then
    echo "Unsafe coordinator completion-loop contract remains: $forbidden_contract" >&2
    exit 1
  fi
done

for contract in \
  'Task ID and Dispatch ID exactly match' \
  'Process a valid message completely before acknowledging' \
  'reconcile existing records idempotently before creating' \
  'Dependency direction is semantic' \
  'An open A does not block B' \
  'Blocker-first DAG progression' \
  'actionable blocker is executable work' \
  'Parent assignment is outcome ownership' \
  'ownership as a live lease' \
  'first unproven gate' \
  'partly done.*are forbidden' \
  'parent coordinator remains active until accepted' \
  'tracker frontier' \
  'valid claim is two-sided durable evidence' \
  'immediately before acceptance' \
  'linked issue titles in human-facing maps' \
  'exactly one canonical issue' \
  'GitHub issue readability is part of durable convergence' \
  'malformed sections as a failed half-mutation' \
  'Dispatch ready blocker Tasks before' \
  'Parent completion is a graph-wide condition' \
  'Ignored `.omx/` plans.*scratch space' \
  'invalidates affected approval' \
  'worktree is clean and all intended changes are committed' \
  'dependency graphs permit it.*writes/resources are' \
  'issues remain open until the authoritative post-merge alert' \
  'Mandatory exit audit'; do
  grep -Eiq "$contract" "$state_machine" || {
    echo "Missing coordinator failure-state contract: $contract" >&2
    exit 1
  }
done

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
  --depends-on 121 --path "$defect_path" \
  --branch subfeature/120/122-plan-review-defect --worktree-id "fixture::$defect_path"

printf 'tax\n' > "$tax_path/tax.txt"
git -C "$tax_path" add tax.txt
git -C "$tax_path" commit -m 'feat(billing): add tax system' >/dev/null

"$tool" set-status --issue 121 --status completed
"$tool" integrate --issue 121
"$tool" sync --issue 122

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

# Native Orca and Codex feature workflow

## 1. Intake and initialize

Read the GitHub parent, sub-issues, dependencies, labels, and comments through
GitHub MCP. Add `agent:claimed` while retaining `agent:ready`, then re-read after
the lifecycle workflow runs. Continue only when `agent:claimed` is the sole
lifecycle label.

Initialize or adopt the parent feature branch with `tools/orca-feature init`.
Create or bind exactly one Orca Run for the parent. Do not create a Run per
sub-issue.

## 2. Dedicated planning and plan review

Create a read-only planning Task whose spec contains:

- `ROLE: feature planner`
- `REQUIRED TECK SKILL: teck-feature-planner`
- `REQUIRED OMX ROLE: planner`
- parent issue and repository identity
- required leaves, dependencies, acceptance criteria, validation, overlap
  risks, and plan-defect output
- explicit prohibition on code, Git, GitHub writes, worktrees, and delegation

Start it natively:

```bash
orca orchestration worker-start --task <planning-task-id> \
  --worktree active --agent codex --model gpt-5.6-sol --effort xhigh --json
```

Wait for authoritative `worker_done`. Then create a separate plan-review Task
using `teck-plan-reviewer`, the OMX `critic` role, Sol/xhigh, and the planner's
artifact. The coordinator does not review the plan itself.

For every actionable plan finding, create a GitHub sub-issue, attach it to the
parent, add a native blocker edge to the affected leaf or parent, and mirror
that dependency in Orca. Dispatch findings to an executor and repeat plan
review until clean.

Only after clean plan review may the coordinator create or reconcile executable
GitHub sub-issues and their Orca Tasks. Re-read before and after every mutation;
never duplicate an existing issue or edge.

## 3. Dispatch executable leaves

Start only Tasks reported ready by Orca and unblocked in GitHub. Create the
native Orca child worktree from the current verified parent feature head, then
register that existing checkout with `tools/orca-feature register`.

Each Task spec contains:

- `ROLE: feature executor`
- `REQUIRED TECK SKILL: teck-feature-executor`
- `REQUIRED OMX ROLE: executor`
- exact GitHub sub-issue, worktree, scope, acceptance criteria, and validation
- conventional local checkpoint commit and exactly-one `worker_done` contract
- prohibitions on push, merge, GitHub mutation, worktree creation, and Orca
  lifecycle mutation beyond injected completion and question commands

Start it natively with Terra/high:

```bash
orca orchestration worker-start --task <leaf-task-id> \
  --worktree id:<full-worktree-id> --agent codex \
  --model gpt-5.6-terra --effort high --json
```

An executor may spawn bounded native Codex subagents for independent work.
Mechanical or exploration helpers default to Luna/xhigh; implementation or
debugging helpers use Terra/high. They edit only the leaf worktree and cannot
commit or send lifecycle messages. Never parallelize overlapping files,
generated outputs, databases, ports, indexes, or mutable services.

## 4. Review and finding repair

After executor `worker_done`, require a clean worktree, local commit, and
validation evidence. Create a separate review Task against the same leaf issue
and worktree using `teck-code-reviewer`, OMX `code-reviewer`, and Sol/xhigh.

Every actionable finding becomes a new GitHub sub-issue under the parent and a
native blocker of the affected implementation issue. Create a corresponding
Orca Task dependency. Reuse the affected leaf worktree sequentially for its
finding executor so the unintegrated leaf state remains available; never run
the reviewer and repair executor concurrently. Repeat review until clean.

## 5. Integrate and synchronize

Only the coordinator integrates a reviewed leaf with
`tools/orca-feature integrate`. Run targeted checks, comment on the GitHub leaf
with the integrated SHA and evidence, close it, and re-read GitHub dependencies
before releasing blocked work.

Release the settled Dispatch, remove the native child through Orca, then call
`tools/orca-feature remove`. Never substitute raw `git worktree remove` or
terminal closure for Orca-owned cleanup.

## 6. Final QA and PR

After all leaves are integrated, create a final QA Task in the parent worktree
using `teck-feature-qa`, OMX `qa-tester` plus `verifier`, and Sol/xhigh. QA is
read-only and reviews the entire integrated feature against the parent issue,
approved plan, repository rules, and required validation.

Every actionable QA finding becomes a GitHub sub-issue blocking the parent and
an Orca Task dependency. Repair it in a new native child worktree through an
executor, integrate it, then run final QA again.

When QA is clean, run proportional feature gates, normally
`nx affected -t build test lint typecheck`. Require `tools/orca-feature pr-info`
to report ready. Publish the parent branch, create one PR with GitHub MCP,
request human review, inspect CI, post evidence, and apply `agent:in-review`
only after required checks are green. Stop with the PR open for a human merge.

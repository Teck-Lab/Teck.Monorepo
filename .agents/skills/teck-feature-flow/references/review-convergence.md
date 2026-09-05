# Review convergence

Apply this contract to plan review, review units, final QA, and every repair.

## Frozen acceptance contract

Clean plan review freezes parent scope, acceptance, constraints, work kind,
validation profile, review units, and plan digest. Later reviewers verify that
contract; they cannot amend it. A new capability, threat model, benchmark,
evidence format, platform, or quality target is scope expansion.

Only an explicit coordinator decision gate may approve expansion. Approval
creates a new plan version and requires fresh plan review. Otherwise record the
proposal as a non-blocking follow-up.

Default to at most seven executable leaves and dependency depth four. Exceeding
either needs written complexity justification and coordinator approval. Do not
create benchmark programs, exact-body capture, digest chains, replay systems,
or proof infrastructure unless a parent criterion requires that specific risk
reduction.

## Finding classification

Every finding includes the fields in `delegation-contracts.md`.

- `blocking-defect`: reproducible incorrectness, security violation, data-loss
  risk, or failure of an explicit criterion or mandatory repository rule.
- `bounded-omission`: evidence or coverage explicitly required by the frozen
  contract is absent.
- `scope-expansion`: useful work outside the frozen contract.
- `observation`: preference, optional hardening, style, or nit.

The first two are actionable only with an exact violated contract,
reproducible evidence, and an in-scope minimal repair. The latter two never
block CLEAN. The reviewer bears the burden of proof; “could be more robust” or
a speculative future use is insufficient.

## Stable findings and limits

Build `finding-key` from review stage, reviewed issue, affected component, and
normalized failure mode. Reuse or reopen one GitHub issue and Orca Task for the
key; a rephrased finding does not create new state.

Permit at most two automatic repair/re-review cycles for one finding key and
three `FINDINGS_PRESENT` verdicts at one review stage. At either limit, prohibit
another automatic repair and create a convergence audit plus native Orca
decision gate. Record attempts and group duplicate findings. Gate options are:

1. narrow or correct the existing repair contract;
2. accept when only non-blocking work remains;
3. approve scope expansion and restart at planning; or
4. escalate a genuine owner decision.

The audit must not create another Task merely to keep the loop moving.

## Review units

Review coherent changes, not scheduling Tasks. One executable GitHub sub-issue
is one review unit and owns one direct child worktree and branch beneath the
main feature worktree. Member Tasks execute sequentially and accumulate scoped
commits there. Separate dependency-unblocked, resource-safe sub-issues may
execute concurrently in their own child worktrees. The coordinator integrates
a sub-issue only after its combined tip receives CLEAN review.

Never share an editable worktree across sub-issues or create nested editable
children for member Tasks. If planned work is independently executable enough
to need concurrent editing, represent it as another reviewed GitHub sub-issue,
give it a native blocker relationship when ordering exists, and create its own
direct child worktree. This keeps GitHub, Orca Task, review, branch, and
worktree identity aligned one-to-one.

- `implementation`: contributes code to its sub-issue review unit.
- `supporting`: research, fixtures, inventories, or generated inputs validated
  by its consuming unit; no standalone code review.
- `repair`: modifies the existing rejected sub-issue worktree and triggers
  fresh review of that branch's new SHA; never creates another review unit.
- `integration`: reviewed with its coherent sub-issue or whole-feature QA unless
  it introduces independently meaningful logic.

Small features normally have one executable sub-issue and review unit. Medium
or large features use one unit per coherent, independently integratable
subfeature, especially for security-sensitive or dependency-gating work.

Plan review checks minimality, feasibility, dependency safety, one-to-one
sub-issue/worktree placement, and proportional evidence. Code review checks an
exact sub-issue branch-tip SHA against its contract. QA checks the entire
integrated parent SHA against observable parent acceptance. QA does not reopen
implementation preferences without new reproducible evidence.

For the `agent-workflow` validation profile, normal evidence is static contract tests plus one
representative dry run. Do not require a new empirical measurement platform
unless the parent explicitly asks for one.

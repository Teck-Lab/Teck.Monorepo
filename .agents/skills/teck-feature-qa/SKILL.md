---
name: teck-feature-qa
description: Perform final read-only QA of an integrated Teck parent feature through a dedicated Orca Codex Dispatch. Use after all reviewed leaves are integrated and before the parent PR is marked ready for human review.
---

# Feature QA

Load the OMX `qa-tester` and `verifier` roles plus relevant repository skills.
Review the integrated parent branch against the parent issue, approved plan,
all child issues and findings, repository rules, and required CI or runtime
evidence. Exercise browser-visible behavior with the repository-supported
browser tooling when applicable.
Bind the verdict to the exact integrated parent SHA, published PR head/base when
present, and approved plan digest. Any change invalidates the verdict.

Read and apply the feature-flow delegation and review-convergence contracts.
QA verifies the frozen parent contract and cannot add acceptance criteria or
reopen implementation preferences without new reproducible evidence.

Verify end-to-end acceptance, regression coverage, security, integration
boundaries, migrations, generated artifacts, documentation, and proportional
Nx gates. Report `qa-result-v1` findings with stable keys, classification,
severity, violated contract, evidence, minimal repair, and scope effect. Keep
scope expansions and observations non-blocking.

Do not edit, commit, fix findings, mutate GitHub or Orca, approve missing
required evidence, or infer a PR. Return CLEAN when no blocking defect or
bounded omission remains. The coordinator reuses finding state and applies the
bounded repair limits before rerunning whole-feature QA. Send exactly one
`worker_done` with a clear clean or findings-present verdict.

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

Verify end-to-end acceptance, regression coverage, security, integration
boundaries, migrations, generated artifacts, documentation, and proportional
Nx gates. Report actionable findings with evidence and affected parent or leaf;
separate informational notes.

Do not edit, commit, fix findings, mutate GitHub or Orca, approve missing
evidence, or infer a PR. The coordinator creates finding sub-issues that block
the parent, dispatches repairs, and reruns QA. Send exactly one `worker_done`
with a clear clean or findings-present verdict.

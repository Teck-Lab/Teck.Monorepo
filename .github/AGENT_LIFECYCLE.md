# Agent issue lifecycle

GitHub issues are the durable admission queue for Orca feature orchestration.
Exactly one lifecycle label may be active on a managed issue:

```text
unmanaged -> agent:ready -> agent:claimed -> agent:in-review -> agent:completed
                                |                  |
                                +-> agent:needs-input <-+
```

`agent:needs-input` can return to `agent:ready` for a new intake or to
`agent:claimed` when the existing orchestrator resumes.

## Ownership

- A person or deterministic GitHub workflow applies `agent:ready` after the
  issue is sufficiently specified and approved for agent work.
- The repository's structured Feature, Bug, Plan defect, and Maintenance issue
  forms require a readiness attestation and apply `agent:ready` at submission.
  Use a draft Project item instead when work still needs product triage.
- Orca intake is the only automation that applies `agent:claimed`. It must
  claim the issue before creating a feature workspace or orchestration Run.
- The feature orchestrator applies `agent:needs-input` when blocked on a human
  decision and `agent:in-review` after opening the final PR.
- Closing a managed issue applies `agent:completed`. Reopening one applies
  `agent:needs-input` so work is never restarted without triage.

The labels describe workflow state only. Security severity, language,
ecosystem, and component/area remain independent reusable labels.

Actors request a transition by adding the target label without first removing
the current label. The lifecycle workflow validates the current/target pair and
then removes the old label. This preserves enough state to reject invalid
transitions. Run only one Orca intake dispatcher per repository; after adding
`agent:claimed`, it must re-read the issue and proceed only after the workflow
has normalized the issue to that single lifecycle label.

## GitHub workflow

`.github/workflows/agent-issue-lifecycle.yml` serializes changes per issue,
rejects invalid state changes, and removes stale lifecycle labels after valid
transitions. It mirrors the normalized lifecycle state into the `Teck Scrum`
Project (`Ready`, `In progress`, `Blocked`, `In review`, or `Done`). Its manual
`workflow_dispatch` entry synchronizes label definitions and backfills every
managed issue into the Project.

The workflow deliberately does not start coding agents. Orca polls for
`agent:ready`, claims an issue, creates its per-workspace environment, and
starts the Atlas feature orchestrator. This keeps GitHub Actions deterministic
and keeps multi-agent execution in Orca.

The `Teck Scrum` organization Project is the visual planning layer. Security
alerts are converted into durable issues by `security-alert-intake.yml`, added
to the Project, and initialized from their severity and component metadata.
Code-scanning and Dependabot findings enter `agent:ready`; sanitized secret
scanning findings enter `agent:needs-input` because credential rotation needs
a person. The workflow reconciles tracking issues only after the underlying
alert is no longer open.

General stale-issue automation must exempt all five lifecycle labels so it
cannot close active or human-blocked agent work.

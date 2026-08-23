# Orca handoff contract

Use a handoff only to transfer an unfinished coordinator or worker session. It
does not replace an Orca Task spec, Dispatch, result artifact, or lifecycle
message. Save it durably where the receiving session can read it and reference
existing issues, plans, commits, and artifacts instead of copying them.

```xml
<handoff version="1">
  <routing>
    <work-kind>bug-fix|feature|security-fix|maintenance|build-config|agent-workflow|docs|research</work-kind>
    <workflow-stage>intake|planning|plan-review|execution|code-review|integration|qa|coordination</workflow-stage>
    <route>WORK_KIND:WORKFLOW_STAGE</route>
  </routing>
  <purpose>What the receiving session must continue.</purpose>
  <anchor>
    <repository>OWNER/REPO</repository>
    <branch>BRANCH</branch>
    <sha>HEX</sha>
    <captured-at>ISO-8601</captured-at>
  </anchor>
  <verified-facts>Claims paired with their read-only probe or durable source.</verified-facts>
  <unverified-assumptions>Inherited claims that must be rechecked.</unverified-assumptions>
  <active-orca-state>Run, Task, Dispatch, terminal, delivery, gate, and lease identities.</active-orca-state>
  <durable-references>Issue, plan, review, commit, diff, and worktree pointers.</durable-references>
  <next-action>One exact resumable action and its preconditions.</next-action>
  <suggested-skills>Skills the receiver should load.</suggested-skills>
</handoff>
```

The two routing axes tell the receiver both what kind of change is in flight
and which role-phase it must resume. Derive `route` from them; never choose it
independently. Recompute routing when a handoff crosses a stage boundary.

Redact credentials and personal data. On resume, compare the anchor with live
Git and Orca state, re-probe every unverified assumption, process existing
deliveries before creating state, and continue from the first unproven gate.

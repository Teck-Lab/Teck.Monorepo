# Orca handoff contract

Use a handoff only to transfer an unfinished coordinator or worker session. It
does not replace an Orca Task spec, Dispatch, result artifact, or lifecycle
message. Save it durably where the receiving session can read it and reference
existing issues, plans, commits, and artifacts instead of copying them.

```xml
<handoff version="1">
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

Redact credentials and personal data. On resume, compare the anchor with live
Git and Orca state, re-probe every unverified assumption, process existing
deliveries before creating state, and continue from the first unproven gate.

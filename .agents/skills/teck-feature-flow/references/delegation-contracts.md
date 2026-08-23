# Delegation and result contracts

Use these versioned envelopes in Orca Task specs and worker report artifacts.
Orca's injected preamble exclusively owns Task/Dispatch identity, heartbeat,
`ask`, and `worker_done`; never copy those commands into a Task contract.

## Task contract

```xml
<task-contract version="1">
  <role>planner|plan-reviewer|executor|code-reviewer|qa</role>
  <objective>One bounded outcome.</objective>
  <sources>
    <parent-issue href="https://github.com/OWNER/REPO/issues/NUMBER" />
    <task-issue href="https://github.com/OWNER/REPO/issues/NUMBER" />
    <approved-plan href="URL_OR_PATH" digest="sha256:HEX" />
  </sources>
  <scope>What this worker owns.</scope>
  <acceptance>Observable completion criteria.</acceptance>
  <validation>Required commands or evidence.</validation>
  <constraints>Boundaries, dependencies, and resource ownership.</constraints>
  <permissions>Explicit allowed mutations; everything else remains prohibited.</permissions>
  <result-contract>plan-result-v1|implementation-result-v1|review-result-v1|qa-result-v1</result-contract>
</task-contract>
```

Omit `task-issue` only for parent-level planning or QA. Omit `approved-plan`
only when the planner is creating the first version. Long criteria stay on the
canonical GitHub issue or plan; the contract points to them and states only the
worker-specific boundary.

## Plan result

```xml
<plan-result version="1">
  <feature-class>product-code|build-config|agent-workflow|docs-research</feature-class>
  <plan-digest>sha256:HEX</plan-digest>
  <review-units>Named units and their member Tasks.</review-units>
  <dependency-waves>Shallow executable waves.</dependency-waves>
  <validation-strategy>Evidence proportional to the feature class.</validation-strategy>
  <decisions>Unresolved owner decisions, or none.</decisions>
</plan-result>
```

## Implementation result

```xml
<implementation-result version="1">
  <outcome>succeeded|failed</outcome>
  <base-sha>HEX</base-sha>
  <tip-sha>HEX</tip-sha>
  <files-modified>Paths, or none.</files-modified>
  <validation-evidence>Commands and outcomes.</validation-evidence>
  <remaining-risks>Risks, or none.</remaining-risks>
</implementation-result>
```

## Review result

```xml
<review-result version="1">
  <verdict>CLEAN|FINDINGS_PRESENT</verdict>
  <reviewed-sha>HEX</reviewed-sha>
  <plan-digest>sha256:HEX</plan-digest>
  <findings>
    <finding>
      <finding-key>STABLE_KEY</finding-key>
      <classification>blocking-defect|bounded-omission|scope-expansion|observation</classification>
      <severity>critical|high|medium|low|informational</severity>
      <violated-contract>Exact criterion or rule; none for non-blocking work.</violated-contract>
      <evidence>Reproducible evidence.</evidence>
      <minimal-repair>Smallest in-scope repair; follow-up for expansion.</minimal-repair>
      <scope-effect>within-scope|expands-scope</scope-effect>
    </finding>
  </findings>
  <follow-ups>Non-blocking recommendations, or none.</follow-ups>
</review-result>
```

`CLEAN` means there is no `blocking-defect` or `bounded-omission`. Recommendations
may remain in `follow-ups`. `FINDINGS_PRESENT` requires at least one actionable
finding with a violated contract, evidence, and within-scope minimal repair.

## QA result

```xml
<qa-result version="1">
  <verdict>CLEAN|FINDINGS_PRESENT</verdict>
  <integrated-sha>HEX</integrated-sha>
  <plan-digest>sha256:HEX</plan-digest>
  <acceptance-evidence>Parent criteria and observed results.</acceptance-evidence>
  <findings>Same finding schema as review-result.</findings>
  <follow-ups>Non-blocking recommendations, or none.</follow-ups>
</qa-result>
```

Save the result envelope in the worker report artifact and pass its path through
the injected `worker_done` report-path field. A formatting-only validation
failure asks the same worker/session to re-emit the artifact; it never repeats
implementation or creates a finding issue.

#!/usr/bin/env bash
set -euo pipefail

validator=tools/teck-agent-contract
fixture_dir="$(mktemp -d)"
trap 'rm -rf "$fixture_dir"' EXIT

cat >"$fixture_dir/clean-review.xml" <<'XML'
<review-result version="1">
  <routing><work-kind>agent-workflow</work-kind><workflow-stage>code-review</workflow-stage><route>agent-workflow:code-review</route></routing>
  <verdict>CLEAN</verdict><reviewed-sha>abc</reviewed-sha><plan-digest>sha256:def</plan-digest>
  <findings><finding><finding-key>review:482:workflow:optional-metric</finding-key><classification>scope-expansion</classification><severity>low</severity><violated-contract>none</violated-contract><evidence>Optional improvement.</evidence><minimal-repair>follow-up</minimal-repair><scope-effect>expands-scope</scope-effect></finding></findings>
  <follow-ups>Consider measuring later.</follow-ups>
</review-result>
XML
"$validator" "$fixture_dir/clean-review.xml" >/dev/null

cat >"$fixture_dir/contradictory.xml" <<'XML'
<review-result version="1">
  <routing><work-kind>agent-workflow</work-kind><workflow-stage>code-review</workflow-stage><route>agent-workflow:code-review</route></routing>
  <verdict>CLEAN</verdict><reviewed-sha>abc</reviewed-sha><plan-digest>sha256:def</plan-digest>
  <findings><finding><finding-key>review:482:workflow:broken</finding-key><classification>blocking-defect</classification><severity>high</severity><violated-contract>criterion 2</violated-contract><evidence>reproduction</evidence><minimal-repair>bounded fix</minimal-repair><scope-effect>within-scope</scope-effect></finding></findings>
  <follow-ups>none</follow-ups>
</review-result>
XML
if "$validator" "$fixture_dir/contradictory.xml" >/dev/null 2>&1; then
  echo "expected contradictory CLEAN result to fail" >&2
  exit 1
fi

cat >"$fixture_dir/expansion-only.xml" <<'XML'
<review-result version="1">
  <routing><work-kind>agent-workflow</work-kind><workflow-stage>code-review</workflow-stage><route>agent-workflow:code-review</route></routing>
  <verdict>FINDINGS_PRESENT</verdict><reviewed-sha>abc</reviewed-sha><plan-digest>sha256:def</plan-digest>
  <findings><finding><finding-key>review:482:workflow:benchmark</finding-key><classification>scope-expansion</classification><severity>medium</severity><violated-contract>none</violated-contract><evidence>Would improve measurement.</evidence><minimal-repair>follow-up</minimal-repair><scope-effect>expands-scope</scope-effect></finding></findings>
  <follow-ups>benchmark</follow-ups>
</review-result>
XML
if "$validator" "$fixture_dir/expansion-only.xml" >/dev/null 2>&1; then
  echo "expected expansion-only FINDINGS_PRESENT result to fail" >&2
  exit 1
fi

cat >"$fixture_dir/duplicate-findings.xml" <<'XML'
<review-result version="1">
  <routing><work-kind>agent-workflow</work-kind><workflow-stage>code-review</workflow-stage><route>agent-workflow:code-review</route></routing>
  <verdict>FINDINGS_PRESENT</verdict><reviewed-sha>abc</reviewed-sha><plan-digest>sha256:def</plan-digest>
  <findings>
    <finding><finding-key>review:482:workflow:loop</finding-key><classification>blocking-defect</classification><severity>high</severity><violated-contract>criterion 1</violated-contract><evidence>first</evidence><minimal-repair>bounded fix</minimal-repair><scope-effect>within-scope</scope-effect></finding>
    <finding><finding-key>review:482:workflow:loop</finding-key><classification>blocking-defect</classification><severity>high</severity><violated-contract>criterion 1</violated-contract><evidence>rephrased</evidence><minimal-repair>same fix</minimal-repair><scope-effect>within-scope</scope-effect></finding>
  </findings>
  <follow-ups>none</follow-ups>
</review-result>
XML
if "$validator" "$fixture_dir/duplicate-findings.xml" >/dev/null 2>&1; then
  echo "expected duplicate finding keys to fail" >&2
  exit 1
fi

cat >"$fixture_dir/task.xml" <<'XML'
<task-contract version="1">
  <routing><work-kind>agent-workflow</work-kind><workflow-stage>execution</workflow-stage><route>agent-workflow:execution</route></routing>
  <role>executor</role><objective>Implement bounded change.</objective>
  <sources><parent-issue href="https://github.com/Teck-Lab/Teck.Monorepo/issues/482" /></sources>
  <scope>one unit</scope><acceptance>criterion</acceptance><validation>test</validation>
  <development-mode>tdd</development-mode><tdd-boundary>focused behavior</tdd-boundary>
  <constraints>bounded</constraints><execution-mode>shared-durable</execution-mode>
  <model-route>codex/gpt-5.6-terra/high</model-route><permissions>worktree edits</permissions>
  <result-contract>implementation-result-v1</result-contract>
</task-contract>
XML
"$validator" "$fixture_dir/task.xml" >/dev/null

sed 's#shared-durable#ephemeral-helper#' "$fixture_dir/task.xml" >"$fixture_dir/hidden-helper.xml"
if "$validator" "$fixture_dir/hidden-helper.xml" >/dev/null 2>&1; then
  echo "expected hidden ephemeral helper mode to fail" >&2
  exit 1
fi

sed 's#shared-durable#parallel-child#' "$fixture_dir/task.xml" >"$fixture_dir/feature-parallel-child.xml"
"$validator" "$fixture_dir/feature-parallel-child.xml" >/dev/null

sed '/<development-mode>/d' "$fixture_dir/task.xml" >"$fixture_dir/executor-without-mode.xml"
if "$validator" "$fixture_dir/executor-without-mode.xml" >/dev/null 2>&1; then
  echo "expected executor task without development mode to fail" >&2
  exit 1
fi

cat >"$fixture_dir/tdd-implementation.xml" <<'XML'
<implementation-result version="1">
  <routing><work-kind>bug-fix</work-kind><workflow-stage>execution</workflow-stage><route>bug-fix:execution</route></routing>
  <outcome>succeeded</outcome><base-sha>abc</base-sha><tip-sha>def</tip-sha><files-modified>src/a.cs</files-modified>
  <development-mode>tdd</development-mode><tdd-boundary>hostile input is absent from logs</tdd-boundary>
  <tdd-evidence><red>focused test failed</red><green>focused test passed</green><refactor>none; focused test passed</refactor></tdd-evidence>
  <validation-evidence>affected tests passed</validation-evidence><remaining-risks>none</remaining-risks>
</implementation-result>
XML
"$validator" "$fixture_dir/tdd-implementation.xml" >/dev/null

sed 's#<red>focused test failed</red>##' "$fixture_dir/tdd-implementation.xml" >"$fixture_dir/tdd-without-red.xml"
if "$validator" "$fixture_dir/tdd-without-red.xml" >/dev/null 2>&1; then
  echo "expected TDD result without red evidence to fail" >&2
  exit 1
fi

cat >"$fixture_dir/validation-only-implementation.xml" <<'XML'
<implementation-result version="1">
  <routing><work-kind>docs</work-kind><workflow-stage>execution</workflow-stage><route>docs:execution</route></routing>
  <outcome>succeeded</outcome><base-sha>abc</base-sha><tip-sha>def</tip-sha><files-modified>docs/a.md</files-modified>
  <development-mode>required-validation-only</development-mode><tdd-boundary>documentation link checker</tdd-boundary>
  <validation-only-evidence><exception-reason>No executable behavior exists.</exception-reason><before>broken link observed</before><after>link checker passed</after></validation-only-evidence>
  <validation-evidence>docs checks passed</validation-evidence><remaining-risks>none</remaining-risks>
</implementation-result>
XML
"$validator" "$fixture_dir/validation-only-implementation.xml" >/dev/null

cat >"$fixture_dir/architect-task.xml" <<'XML'
<task-contract version="1">
  <routing><work-kind>feature</work-kind><workflow-stage>planning</workflow-stage><route>feature:planning</route></routing>
  <role>delivery-architect</role><objective>Design delivery.</objective>
  <sources><parent-issue href="https://github.com/Teck-Lab/Teck.Monorepo/issues/500" /></sources>
  <scope>manifest</scope><acceptance>complete</acceptance><validation>review</validation><constraints>read-only</constraints>
  <execution-mode>shared-durable</execution-mode><model-route>opus-or-sol/high</model-route>
  <permissions>read-only</permissions><result-contract>delivery-manifest-result-v1</result-contract>
</task-contract>
XML
"$validator" "$fixture_dir/architect-task.xml" >/dev/null

cat >"$fixture_dir/delivery-manifest.xml" <<'XML'
<delivery-manifest-result version="1">
  <routing><work-kind>feature</work-kind><workflow-stage>planning</workflow-stage><route>feature:planning</route></routing>
  <validation-profile>product-code</validation-profile><manifest-digest>sha256:abc</manifest-digest>
  <technical-approach>bounded</technical-approach><sub-issue-drafts>one</sub-issue-drafts><member-tasks>one</member-tasks>
  <expected-change-boundaries>two files</expected-change-boundaries><dependency-waves>one</dependency-waves>
  <review-units>one</review-units><model-routes>Luna/xhigh</model-routes><validation-strategy>tests</validation-strategy>
  <materialization-order>issue then task</materialization-order><decisions>none</decisions>
</delivery-manifest-result>
XML
"$validator" "$fixture_dir/delivery-manifest.xml" >/dev/null

sed 's#delivery-manifest-result-v1#plan-result-v1#' "$fixture_dir/architect-task.xml" >"$fixture_dir/architect-wrong-result.xml"
if "$validator" "$fixture_dir/architect-wrong-result.xml" >/dev/null 2>&1; then
  echo "expected architect task with legacy result to fail" >&2
  exit 1
fi

cat >"$fixture_dir/discovery-task.xml" <<'XML'
<task-contract version="1">
  <routing><work-kind>research</work-kind><workflow-stage>discovery</workflow-stage><route>research:discovery</route></routing>
  <role>discovery-researcher</role><objective>Resolve one product question.</objective>
  <sources><discovery-anchor>Recurring orders discovery conversation</discovery-anchor></sources>
  <scope>official sources</scope><acceptance>cited answer</acceptance><validation>source read-back</validation>
  <constraints>no decisions</constraints><execution-mode>shared-durable</execution-mode>
  <model-route>codex/gpt-5.6-terra/high</model-route><permissions>read-only</permissions>
  <result-contract>discovery-result-v1</result-contract>
</task-contract>
XML
"$validator" "$fixture_dir/discovery-task.xml" >/dev/null
sed 's#shared-durable#parallel-child#' "$fixture_dir/discovery-task.xml" >"$fixture_dir/discovery-parallel-child.xml"
"$validator" "$fixture_dir/discovery-parallel-child.xml" >/dev/null

cat >"$fixture_dir/discovery-result.xml" <<'XML'
<discovery-result version="1">
  <routing><work-kind>research</work-kind><workflow-stage>discovery</workflow-stage><route>research:discovery</route></routing>
  <outcome>succeeded</outcome><question>Can the provider schedule retries?</question>
  <method>official documentation review</method><findings>Retries are supported.</findings>
  <evidence>Provider API reference.</evidence><artifacts>none</artifacts>
  <product-implications>Scheduling can remain provider-backed.</product-implications>
  <unresolved-decisions>Retry policy.</unresolved-decisions>
</discovery-result>
XML
"$validator" "$fixture_dir/discovery-result.xml" >/dev/null

sed 's#<discovery-anchor>.*</discovery-anchor>##' "$fixture_dir/discovery-task.xml" >"$fixture_dir/discovery-without-anchor.xml"
if "$validator" "$fixture_dir/discovery-without-anchor.xml" >/dev/null 2>&1; then
  echo "expected discovery task without an anchor to fail" >&2
  exit 1
fi

sed 's#discovery-result-v1#implementation-result-v1#' "$fixture_dir/discovery-task.xml" >"$fixture_dir/discovery-wrong-result.xml"
if "$validator" "$fixture_dir/discovery-wrong-result.xml" >/dev/null 2>&1; then
  echo "expected discovery task with engineering result contract to fail" >&2
  exit 1
fi

sed 's#agent-workflow:execution#feature:plan-review#' "$fixture_dir/task.xml" >"$fixture_dir/mismatched-route.xml"
if "$validator" "$fixture_dir/mismatched-route.xml" >/dev/null 2>&1; then
  echo "expected a route that disagrees with its axes to fail" >&2
  exit 1
fi

sed 's#<work-kind>agent-workflow</work-kind>#<work-kind>unknown</work-kind>#' "$fixture_dir/task.xml" >"$fixture_dir/unknown-kind.xml"
if "$validator" "$fixture_dir/unknown-kind.xml" >/dev/null 2>&1; then
  echo "expected unknown work-kind to fail" >&2
  exit 1
fi

sed 's#<role>executor</role>#<role>executor</role><task-id>task_stale</task-id>#' "$fixture_dir/task.xml" >"$fixture_dir/duplicated-lifecycle.xml"
if "$validator" "$fixture_dir/duplicated-lifecycle.xml" >/dev/null 2>&1; then
  echo "expected duplicated Orca lifecycle identity to fail" >&2
  exit 1
fi

echo "teck agent contract tests passed"

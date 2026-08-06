import test from "node:test";
import assert from "node:assert/strict";
import {
  componentForPath,
  fingerprint,
  fingerprintFromBody,
  issueBody,
  normalizeCodeScanning,
  normalizeDependabot,
  normalizeSecretScanning,
  priorityForSeverity,
  severity,
} from "./security-alert-intake.mjs";

test("normalizes security severities to board priorities", () => {
  assert.equal(severity("warning"), "medium");
  assert.equal(severity("error"), "high");
  assert.equal(severity("moderate"), "medium");
  assert.equal(priorityForSeverity("critical"), "Urgent");
});

test("maps monorepo paths to project components", () => {
  assert.equal(componentForPath("src/services/commerce/catalog/a.cs"), "Commerce");
  assert.equal(componentForPath("src/apps/admin/page.tsx"), "Web");
  assert.equal(componentForPath(".github/workflows/ci.yml"), "Infrastructure");
});

test("round-trips the stable fingerprint marker", () => {
  const finding = { source: "code-scanning", number: 42, url: "https://example.test", details: [] };
  const body = issueBody(finding, "Teck-Lab", "Teck.Monorepo");
  assert.equal(fingerprintFromBody(body), fingerprint("Teck-Lab", "Teck.Monorepo", "code-scanning", 42));
});

test("normalizes code scanning without exposing API internals", () => {
  const finding = normalizeCodeScanning({ number: 2, html_url: "https://example.test/2", rule: { description: "SQL injection", security_severity_level: "high", id: "cs/sql" }, tool: { name: "CodeQL" }, most_recent_instance: { location: { path: "src/services/commerce/order/a.cs" } } }, "Teck-Lab/Teck.Monorepo");
  assert.equal(finding.component, "Commerce");
  assert.equal(finding.severity, "high");
});

test("normalizes Dependabot manifests", () => {
  const finding = normalizeDependabot({ number: 3, html_url: "https://example.test/3", dependency: { package: { name: "foo", ecosystem: "npm" }, manifest_path: "src/apps/store/package.json" }, security_advisory: { severity: "critical", summary: "Unsafe foo", ghsa_id: "GHSA-test" } }, "Teck-Lab/Teck.Monorepo");
  assert.equal(finding.component, "Web");
  assert.equal(finding.severity, "critical");
});

test("secret issues are sanitized and require restricted alert review", () => {
  const finding = normalizeSecretScanning({ number: 4, html_url: "https://example.test/4", secret: "must-not-leak", secret_type_display_name: "API token", validity: "active" }, "Teck-Lab/Teck.Monorepo");
  const body = issueBody(finding, "Teck-Lab", "Teck.Monorepo");
  assert.equal(body.includes("must-not-leak"), false);
  assert.match(body, /intentionally omitted/);
  assert.equal(finding.severity, "critical");
});

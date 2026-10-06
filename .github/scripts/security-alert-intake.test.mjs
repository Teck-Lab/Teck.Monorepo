import assert from "node:assert/strict";
import { createHmac } from "node:crypto";
import test from "node:test";
import {
  applyDependabotRisk,
  buildPaperclipSnapshot,
  componentForPath,
  fingerprint,
  githubSignature,
  normalizeCodeScanning,
  normalizeDependabot,
  normalizeSecretScanning,
  priorityForFinding,
  priorityForSeverity,
  severity,
} from "./security-alert-intake.mjs";

test("normalizes security severities to board priorities", () => {
  assert.equal(severity("warning"), "medium");
  assert.equal(severity("error"), "high");
  assert.equal(severity("moderate"), "medium");
  assert.equal(priorityForSeverity("critical"), "Urgent");
  assert.equal(priorityForFinding({ severity: "medium", epss: 0.6 }), "Urgent");
  assert.equal(priorityForFinding({ severity: "low", epss: 0.2 }), "High");
});

test("maps monorepo paths to project components", () => {
  assert.equal(
    componentForPath("src/services/commerce/catalog/a.cs"),
    "Commerce",
  );
  assert.equal(componentForPath("src/apps/admin/page.tsx"), "Web");
  assert.equal(componentForPath(".github/workflows/ci.yml"), "Infrastructure");
});

test("normalizes code scanning without exposing API internals", () => {
  const finding = normalizeCodeScanning(
    {
      number: 2,
      html_url: "https://example.test/2",
      rule: {
        description: "SQL injection",
        security_severity_level: "high",
        id: "cs/sql",
      },
      tool: { name: "CodeQL" },
      most_recent_instance: {
        location: { path: "src/services/commerce/order/a.cs" },
      },
    },
    "Teck-Lab/Teck.Monorepo",
  );
  assert.equal(finding.component, "Commerce");
  assert.equal(finding.severity, "high");
});

test("normalizes and enriches Dependabot findings", () => {
  const finding = normalizeDependabot(
    {
      number: 3,
      html_url: "https://example.test/3",
      dependency: {
        package: { name: "foo", ecosystem: "npm" },
        manifest_path: "src/apps/store/package.json",
      },
      security_advisory: {
        severity: "critical",
        summary: "Unsafe foo",
        ghsa_id: "GHSA-test",
        identifiers: [{ type: "CVE", value: "CVE-2026-1234" }],
      },
    },
    "Teck-Lab/Teck.Monorepo",
  );
  applyDependabotRisk(
    finding,
    new Map([["CVE-2026-1234", 0.75]]),
    new Set(["CVE-2026-1234"]),
  );
  assert.equal(finding.component, "Web");
  assert.equal(finding.severity, "critical");
  assert.equal(finding.epss, 0.75);
  assert.equal(finding.kev, true);
});

test("secret findings and the Paperclip snapshot omit the detected secret", () => {
  const finding = normalizeSecretScanning(
    {
      number: 4,
      html_url: "https://example.test/4",
      secret: "must-not-leak",
      locations_url: "https://api.example.test/restricted-location",
      secret_type_display_name: "API token",
      validity: "active",
    },
    "Teck-Lab/Teck.Monorepo",
  );
  const snapshot = buildPaperclipSnapshot(
    "Teck-Lab",
    "Teck.Monorepo",
    [finding],
    new Set(["secret-scanning"]),
    "2026-10-04T00:00:00.000Z",
  );
  const serialized = JSON.stringify(snapshot);
  assert.equal(serialized.includes("must-not-leak"), false);
  assert.equal(serialized.includes("restricted-location"), false);
  assert.equal(snapshot.findings[0].requiresHumanInput, true);
  assert.equal(
    snapshot.findings[0].id,
    fingerprint("Teck-Lab", "Teck.Monorepo", "secret-scanning", 4),
  );
});

test("builds a deterministic authoritative snapshot and GitHub HMAC", () => {
  const snapshot = buildPaperclipSnapshot(
    "Teck-Lab",
    "Teck.Monorepo",
    [
      {
        source: "code-scanning",
        number: 42,
        severity: "high",
        component: "Platform",
        title: "Finding",
        url: "https://example.test/42",
        details: ["Rule: example"],
      },
    ],
    new Set(["secret-scanning", "code-scanning"]),
    "2026-10-04T00:00:00.000Z",
  );
  assert.equal(snapshot.schema, "teck/security-alert-snapshot/v1");
  assert.equal(snapshot.authoritativeSnapshot, true);
  assert.deepEqual(snapshot.availableSources, [
    "code-scanning",
    "secret-scanning",
  ]);
  const body = JSON.stringify(snapshot);
  assert.equal(
    githubSignature(body, "test-secret"),
    `sha256=${createHmac("sha256", "test-secret").update(body).digest("hex")}`,
  );
});

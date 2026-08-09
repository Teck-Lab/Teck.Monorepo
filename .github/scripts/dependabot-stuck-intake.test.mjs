import assert from "node:assert/strict";
import test from "node:test";
import {
  checkState,
  fingerprint,
  fingerprintFromBody,
  indexIssues,
  issueBody,
  linkedPullRequestBody,
} from "./dependabot-stuck-intake.mjs";

const now = Date.parse("2026-08-09T12:00:00Z");
const failedCheck = {
  name: "PR validation",
  status: "completed",
  conclusion: "failure",
  completed_at: "2026-08-09T11:30:00Z",
  html_url: "https://example/check",
};

test("requires terminal failures to outlive the grace period", () => {
  assert.equal(checkState([failedCheck], [], now, 20).state, "stuck");
  assert.equal(
    checkState([{ ...failedCheck, completed_at: "2026-08-09T11:50:00Z" }], [], now, 20).state,
    "young-failure",
  );
  assert.equal(
    checkState([{ ...failedCheck, status: "in_progress", conclusion: null }], [], now, 20).state,
    "pending",
  );
  assert.equal(
    checkState([{ ...failedCheck, conclusion: "success" }], [], now, 20).state,
    "healthy",
  );
  assert.equal(checkState([], [], now, 20).state, "pending");
});

test("includes legacy commit statuses in classification", () => {
  assert.equal(
    checkState(
      [],
      [{ context: "legacy", state: "failure", updated_at: "2026-08-09T11:00:00Z" }],
      now,
      20,
    ).state,
    "stuck",
  );
  assert.equal(
    checkState(
      [],
      [{ context: "legacy", state: "pending", updated_at: "2026-08-09T11:00:00Z" }],
      now,
      20,
    ).state,
    "pending",
  );
});

test("ignores non-gating Dependabot bookkeeping failures", () => {
  assert.equal(
    checkState([{ ...failedCheck, name: "Link alert issue and Project item" }], [], now, 20).state,
    "pending",
  );
  assert.equal(
    checkState([{ ...failedCheck, name: "PR Validation" }], [], now, 20).state,
    "pending",
  );
});

test("uses one stable issue fingerprint per pull request", () => {
  const pull = {
    number: 31,
    html_url: "https://example/pr/31",
    head: { ref: "dependabot/npm/foo", sha: "abc" },
  };
  const body = issueBody("Teck-Lab", "Teck.Monorepo", pull, [
    { name: "CI", url: "https://example/check" },
  ]);
  assert.equal(fingerprintFromBody(body), fingerprint("Teck-Lab", "Teck.Monorepo", 31));
  const issue = { number: 91, body, labels: [] };
  assert.equal(indexIssues([issue]).get("teck-lab/teck.monorepo#31"), issue);
});

test("links the existing PR to exactly one repair issue", () => {
  const first = linkedPullRequestBody(
    "Dependabot details\n\n<!-- another-automation -->\nkeep me",
    91,
  );
  const updated = linkedPullRequestBody(first, 92);
  assert.equal(updated.match(/teck-dependabot-stuck-issue/g)?.length, 1);
  assert.match(updated, /Fixes #92$/);
  assert.doesNotMatch(updated, /Fixes #91/);
  assert.match(updated, /another-automation/);
});

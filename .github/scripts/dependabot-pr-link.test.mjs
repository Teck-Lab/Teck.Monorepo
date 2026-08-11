import assert from "node:assert/strict";
import test from "node:test";
import {
  dependabotIssueMetadata,
  linkedPullRequestBody,
  matchingIssues,
  parseNames,
} from "./dependabot-pr-link.mjs";

const issues = [
  { number: 41, body: "- Package: `foo` (npm)\n- Advisory: GHSA-abcd-1234-5678" },
  { number: 42, body: "- Package: `bar` (npm)\n- Advisory: GHSA-abcd-1234-5678" },
  { number: 43, body: "- Package: `foo` (npm)\n- Advisory: GHSA-other" },
];

test("parses Dependabot issue metadata and dependency lists", () => {
  assert.deepEqual(dependabotIssueMetadata(issues[0].body), {
    advisory: "ghsa-abcd-1234-5678",
    package: "foo",
  });
  assert.deepEqual([...parseNames("foo, @scope/bar")], ["foo", "@scope/bar"]);
});

test("matches an advisory and the dependencies changed by the PR", () => {
  assert.deepEqual(
    matchingIssues(issues, "GHSA-abcd-1234-5678", "foo").map((issue) => issue.number),
    [41],
  );
  assert.deepEqual(
    matchingIssues(issues, "GHSA-abcd-1234-5678", "foo,bar").map((issue) => issue.number),
    [41, 42],
  );
});

test("adds and idempotently refreshes issue closing links", () => {
  const linked = linkedPullRequestBody(
    "Dependabot details\n\n<!-- another-automation -->\nkeep me",
    issues.slice(0, 2),
  );
  assert.match(linked, /Closes #41\nCloses #42/);
  assert.equal(
    linkedPullRequestBody(linked, issues.slice(0, 1)).match(/teck-dependabot-security-links/g)
      ?.length,
    1,
  );
  assert.match(linkedPullRequestBody(linked, issues.slice(0, 1)), /Closes #41/);
  assert.doesNotMatch(linkedPullRequestBody(linked, issues.slice(0, 1)), /Closes #42/);
  assert.match(linkedPullRequestBody(linked, issues.slice(0, 1)), /another-automation/);
});

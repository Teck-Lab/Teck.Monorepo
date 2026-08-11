import assert from "node:assert/strict";
import test from "node:test";
import lint from "@commitlint/lint";
import config, { subjectCaseUnlessDependabot } from "./commitlint.config.mjs";

const parsed = (subject, footer = null) => ({ subject, body: null, footer });

test("keeps lowercase subjects valid", () => {
  assert.equal(subjectCaseUnlessDependabot(parsed("add checkout validation"))[0], true);
});

test("rejects capitalized human and agent subjects", () => {
  assert.equal(subjectCaseUnlessDependabot(parsed("Add checkout validation"))[0], false);
  assert.equal(subjectCaseUnlessDependabot(parsed("Bump ErrorOr from 2.0.1 to 2.1.1"))[0], false);
});

test("accepts Dependabot's signed capitalized subject", () => {
  assert.equal(
    subjectCaseUnlessDependabot(
      parsed(
        "Bump ErrorOr from 2.0.1 to 2.1.1",
        "Signed-off-by: dependabot[bot] <support@github.com>",
      ),
    )[0],
    true,
  );
});

test("does not exempt similar signatures", () => {
  assert.equal(
    subjectCaseUnlessDependabot(
      parsed("Bump ErrorOr from 2.0.1 to 2.1.1", "Signed-off-by: another-bot <support@github.com>"),
    )[0],
    false,
  );
});

test("recognizes Dependabot's trailer after parsing the complete message", async () => {
  const message = `chore: Bump ErrorOr from 2.0.1 to 2.1.1

---
updated-dependencies:
- dependency-name: ErrorOr
  dependency-version: 2.1.1
  dependency-type: direct:production
  update-type: version-update:semver-minor
...

Signed-off-by: dependabot[bot] <support@github.com>`;
  const result = await lint(
    message,
    { "subject-case-unless-dependabot": [2, "always"] },
    { plugins: config.plugins },
  );
  assert.equal(result.valid, true, JSON.stringify(result.errors));
});

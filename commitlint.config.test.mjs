import assert from "node:assert/strict";
import test from "node:test";
import conventional from "@commitlint/config-conventional";
import lint from "@commitlint/lint";
import { lintPullRequestTitle } from "./.github/scripts/check-pr-title.mjs";
import config, { subjectCaseUnlessDependabot } from "./commitlint.config.mjs";

const parsed = (subject, footer = null) => ({ subject, body: null, footer });
const rules = { ...conventional.rules, ...config.rules };
const longDependabotTitle =
  "chore(deps): bump docker/setup-buildx-action from 4.3.0 to 4.4.1 in the actions-minor-patch group across 1 directory";
const dependabotTrailer = "Signed-off-by: dependabot[bot] <support@github.com>";

test("accepts domain, group, package and repository scopes", async () => {
  for (const scope of [
    "order",
    "operations",
    "billing",
    "notification",
    "web",
    "ui",
    "ci",
    "release",
  ]) {
    const result = await lint(`feat(${scope}): add capability`, config.rules, {
      plugins: config.plugins,
    });
    assert.equal(result.valid, true, JSON.stringify(result.errors));
  }
});

test("rejects unknown scopes and raw Nx project names", async () => {
  for (const scope of ["orderr", "Order.Host", "anything"]) {
    const result = await lint(`feat(${scope}): add capability`, config.rules, {
      plugins: config.plugins,
    });
    assert.equal(result.valid, false, scope);
  }
});

test("requires scopes on human and agent commits", async () => {
  const result = await lint("feat: add checkout", config.rules, { plugins: config.plugins });
  assert.equal(result.valid, false);
  assert.ok(result.errors.some((error) => error.name === "scope-required-unless-dependabot"));
});

test("accepts Dependabot's existing unscoped commit format", async () => {
  const result = await lint(
    "chore: Bump ErrorOr from 2.0.1 to 2.1.1\n\nSigned-off-by: dependabot[bot] <support@github.com>",
    config.rules,
    { plugins: config.plugins },
  );
  assert.equal(result.valid, true, JSON.stringify(result.errors));
});

test("accepts comma-separated domain scopes", async () => {
  const result = await lint("fix(order,inventory): correct reservation handling", config.rules, {
    plugins: config.plugins,
  });
  assert.equal(result.valid, true, JSON.stringify(result.errors));
});

test("validates squash PR titles including breaking changes", async () => {
  for (const title of [
    "feat(order)!: change checkout contract",
    "fix(order,inventory): repair reservations",
  ]) {
    const result = await lintPullRequestTitle(title, "contributor");
    assert.equal(result.valid, true, JSON.stringify(result.errors));
  }
  for (const title of [
    "add checkout",
    "feat: add checkout",
    "feat(orderr): add checkout",
    "feat(order): Add checkout",
  ]) {
    const result = await lintPullRequestTitle(title, "contributor");
    assert.equal(result.valid, false, title);
  }
});

test("Dependabot titles keep their subject exemption but still validate scopes", async () => {
  assert.equal((await lintPullRequestTitle("chore: Bump ErrorOr", "dependabot[bot]")).valid, true);
  assert.equal(
    (await lintPullRequestTitle("chore(deps): Bump ErrorOr", "dependabot[bot]")).valid,
    true,
  );
  assert.equal(
    (await lintPullRequestTitle("chore(orderr): Bump ErrorOr", "dependabot[bot]")).valid,
    false,
  );
});

test("accepts existing long and development dependency titles from Dependabot", async () => {
  assert.equal(longDependabotTitle.length, 116);
  for (const title of [
    longDependabotTitle,
    "chore(deps-dev): bump @types/node from 22.20.0 to 26.6.5",
    "chore(deps-dev): bump babel-preset-expo from 57.0.14 to 58.0.12",
  ]) {
    const result = await lintPullRequestTitle(title, "dependabot[bot]");
    assert.equal(result.valid, true, JSON.stringify(result.errors));
  }
});

test("keeps the 100-character PR title limit for humans, agents and similar bot names", async () => {
  const title = `fix(ci): ${"a".repeat(91)}`;
  assert.equal(title.length, 100);
  for (const author of ["contributor", "teck-agent[bot]", "dependabot-preview[bot]"]) {
    assert.equal((await lintPullRequestTitle(title, author)).valid, true);
    const result = await lintPullRequestTitle(`${title}a`, author);
    assert.equal(result.valid, false);
    assert.ok(result.errors.some((error) => error.name === "header-max-length-unless-dependabot"));
    assert.equal((await lintPullRequestTitle(longDependabotTitle, author)).valid, false);
  }
});

test("long Dependabot titles still enforce conventional types and allowed scopes", async () => {
  for (const title of [
    longDependabotTitle.replace("chore(deps)", "unknown(deps)"),
    longDependabotTitle.replace("(deps)", "(unknown)"),
    "this is not a conventional commit",
  ]) {
    assert.equal((await lintPullRequestTitle(title, "dependabot[bot]")).valid, false, title);
  }
});

test("commit length uses the same boundary and only exempts the exact Dependabot trailer", async () => {
  const title = `fix(ci): ${"a".repeat(91)}`;
  assert.equal((await lint(title, rules, { plugins: config.plugins })).valid, true);
  for (const message of [
    `${title}a`,
    `${longDependabotTitle}\n\nSigned-off-by: another-bot <support@github.com>`,
    `${longDependabotTitle}\n\nquoted ${dependabotTrailer}`,
    `${longDependabotTitle}\n\n${dependabotTrailer} trailing text`,
  ]) {
    const result = await lint(message, rules, { plugins: config.plugins });
    assert.equal(result.valid, false);
    assert.ok(result.errors.some((error) => error.name === "header-max-length-unless-dependabot"));
  }
  for (const header of [longDependabotTitle, "chore(deps-dev): Bump @types/node"]) {
    const result = await lint(`${header}\n\n${dependabotTrailer}`, rules, {
      plugins: config.plugins,
    });
    assert.equal(result.valid, true, JSON.stringify(result.errors));
  }
  const unknownScope = await lint(
    `${longDependabotTitle.replace("(deps)", "(unknown)")}\n\n${dependabotTrailer}`,
    rules,
    { plugins: config.plugins },
  );
  assert.equal(unknownScope.valid, false);
  assert.ok(unknownScope.errors.some((error) => error.name === "scope-enum"));
});

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

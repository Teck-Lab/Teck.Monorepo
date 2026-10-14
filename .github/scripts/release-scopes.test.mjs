import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import test from "node:test";
import { expandCommitScopes } from "./release-scopes.mjs";

const require = createRequire(import.meta.url);
const { getCommitsRelevantToProjects } = require("nx/src/command-line/release/utils/shared");
const { determineSemverChange } = require("nx/src/command-line/release/utils/semver");
const { getLatestGitTagForPattern } = require("nx/src/command-line/release/utils/git");
const {
  DEFAULT_CONVENTIONAL_COMMITS_CONFIG,
} = require("nx/src/command-line/release/config/conventional-commits");
const configuredTypes = JSON.parse(readFileSync("nx.json", "utf8")).release.conventionalCommits
  .types;
const conventionalConfig = {
  ...DEFAULT_CONVENTIONAL_COMMITS_CONFIG,
  types: Object.fromEntries(
    Object.entries(DEFAULT_CONVENTIONAL_COMMITS_CONFIG.types).map(([type, defaults]) => [
      type,
      { ...defaults, ...configuredTypes[type] },
    ]),
  ),
};

const projectGraph = {
  nodes: {
    "Billing.Application": {
      name: "Billing.Application",
      data: { root: "src/services/operations/billing/Billing.Application" },
    },
    "Billing.Host": {
      name: "Billing.Host",
      data: { root: "src/services/operations/billing/Billing.Host" },
    },
    "Notification.Host": {
      name: "Notification.Host",
      data: { root: "src/services/operations/notification/Notification.Host" },
    },
    "Order.Host": { name: "Order.Host", data: { root: "src/services/commerce/order/Order.Host" } },
  },
};
const groups = {
  operations: ["Billing.Host", "Notification.Host"],
  order: ["Order.Host"],
};

async function bumps(scope, type = "feat", isBreaking = false, group = "operations") {
  const commits = expandCommitScopes(
    [{ scope, type, isBreaking, message: `${type}(${scope}): change` }],
    projectGraph,
  );
  const relevant = await getCommitsRelevantToProjects(
    projectGraph,
    commits,
    groups[group],
    { git: {}, version: { git: {} }, changelog: { git: {} } },
    { resolveAffectedFilesPerCommitInProjectGraph: async () => projectGraph },
  );
  return determineSemverChange(relevant, conventionalConfig);
}

test("service domain scopes avoid Nx ambiguity and preserve direct minor bumps", async () => {
  const result = await bumps("billing");
  assert.equal(result.get("Billing.Application"), undefined);
  assert.equal(result.get("Billing.Host"), 2);
  assert.equal(result.get("Notification.Host"), 1);
});

test("group scopes apply breaking changes to all directly affected group members", async () => {
  const result = await bumps("operations", "feat", true);
  for (const project of groups.operations) assert.equal(result.get(project), 3);
});

test("other release groups receive indirect patch bumps", async () => {
  const result = await bumps("billing", "feat", true, "order");
  assert.equal(result.get("Order.Host"), 1);
});

test("non-releasable commits do not produce indirect bumps", async () => {
  const result = await bumps("billing", "docs");
  for (const project of groups.operations) assert.equal(result.get(project), null);
});

test("dependency, build, refactor and performance changes patch affected components", async () => {
  for (const type of ["chore", "build", "refactor", "perf", "style", "revert"]) {
    const result = await bumps("billing", type);
    assert.equal(result.get("Billing.Host"), 1);
  }
});

test("historical ambiguous scopes and multiple scopes expand to exact project names", () => {
  const [commit] = expandCommitScopes([{ scope: "Billing,order" }], projectGraph);
  assert.deepEqual(
    new Set(commit.scope.split(",")),
    new Set(["Billing.Application", "Billing.Host", "Order.Host"]),
  );
});

test("native Nx isolates component channels and reuses stable versions before their first candidate", async () => {
  const tags = ["order@v2.0.0-beta.0", "order@v1.3.0-next.1", "order@v1.2.0"];
  for (const [preid, expected] of [
    [undefined, "1.2.0"],
    ["alpha", "1.2.0"],
    ["beta", "2.0.0-beta.0"],
    ["next", "1.3.0-next.1"],
  ]) {
    const resolved = await getLatestGitTagForPattern(
      "order@v{version}",
      {},
      async (allBranches) => {
        assert.equal(allBranches, false);
        return tags;
      },
      { preid, strictPreid: true, requireSemver: true, checkAllBranchesWhen: false },
    );
    assert.equal(resolved.extractedVersion, expected);
  }
});

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import {
  applyRelationship,
  createClient,
  parseInputs,
  validateBranch,
} from "./issue-relationships.mjs";

const repo = "Teck-Lab/Teck.Monorepo";
const otherRepo = "Teck-Lab/Teck.GitOps";
const path = (number, repository = repo) => `repos/${repository}/issues/${number}`;
const issue = (number, repository = repo, id = number) => ({
  id,
  node_id: `I_${id}`,
  number,
  url: `https://api.github.com/${path(number, repository)}`,
});
const defaults = {
  OPERATION: "add",
  ISSUE_NUMBER: "1",
  RELATED_REPOSITORY: "Teck.Monorepo",
  RELATED_ISSUE_NUMBER: "2",
};
const input = (relationship, operation = "add", env = {}) =>
  parseInputs({ ...defaults, RELATIONSHIP: relationship, OPERATION: operation, ...env });

function fakeClient({ ignoreWrites = false } = {}) {
  const issues = new Map([
    [path(1), issue(1)],
    [path(2), issue(2)],
    [path(3), issue(3)],
    [path(2, otherRepo), issue(2, otherRepo, 20)],
  ]);
  const lists = new Map(),
    parents = new Map(),
    writes = [],
    queries = [];
  let branchLinks = [],
    manualPrLinks = [],
    keywordPrLinks = [],
    existingBranch = false;
  const connection = (field, nodes) => ({
    node: { [field]: { nodes, pageInfo: { hasNextPage: false, endCursor: null } } },
  });
  const client = {
    async request(method, endpoint, body) {
      if (method === "GET") {
        if (issues.has(endpoint)) return structuredClone(issues.get(endpoint));
        if (endpoint.endsWith("/parent")) return parents.get(endpoint.slice(0, -7)) ?? null;
        if (endpoint.includes("/pulls/")) return { node_id: "PR_99" };
        if (endpoint === `repos/${repo}` || endpoint === `repos/${otherRepo}`)
          return { node_id: "R_1", default_branch: "main" };
        if (endpoint.endsWith("/git/ref/heads/main"))
          return { object: { sha: "a".repeat(40), type: "commit" } };
        if (endpoint.includes("/git/ref/heads/"))
          return existingBranch ? { ref: "refs/heads/feature/test" } : null;
        throw new Error(`Unexpected fake GET: ${endpoint}`);
      }
      writes.push({ method, endpoint, body });
      if (ignoreWrites) return {};
      if (endpoint.endsWith("/sub_issues")) {
        const parent = issues.get(endpoint.slice(0, -11));
        const child = [...issues.values()].find((item) => item.id === body.sub_issue_id);
        parents.set(child.url.slice("https://api.github.com/".length), parent);
        lists.set(endpoint, [...(lists.get(endpoint) ?? []), child]);
      } else if (endpoint.endsWith("/sub_issue")) {
        const child = [...issues.values()].find((item) => item.id === body.sub_issue_id);
        parents.delete(child.url.slice("https://api.github.com/".length));
        lists.set(
          `${endpoint}s`,
          (lists.get(`${endpoint}s`) ?? []).filter((item) => item.id !== child.id),
        );
      } else if (method === "POST") {
        const related = [...issues.values()].find((item) => item.id === body.issue_id);
        lists.set(endpoint, [...(lists.get(endpoint) ?? []), related]);
      } else if (method === "DELETE") {
        const base = endpoint.slice(0, endpoint.lastIndexOf("/"));
        const id = Number(endpoint.slice(endpoint.lastIndexOf("/") + 1));
        lists.set(
          base,
          (lists.get(base) ?? []).filter((item) => item.id !== id),
        );
      }
      return {};
    },
    async list(endpoint) {
      return structuredClone(lists.get(endpoint) ?? []);
    },
    async graphql(query, variables) {
      queries.push({ query, variables });
      if (query.startsWith("query")) {
        if (query.includes("linkedBranches")) return connection("linkedBranches", branchLinks);
        return connection(
          "closedByPullRequestsReferences",
          query.includes("userLinkedOnly: true")
            ? manualPrLinks
            : [...manualPrLinks, ...keywordPrLinks],
        );
      }
      writes.push({ query, variables });
      if (!ignoreWrites) {
        if (query.includes("createLinkedBranch(")) {
          existingBranch = true;
          branchLinks.push({
            id: "LB_1",
            ref: { name: variables.input.name, repository: { nameWithOwner: otherRepo } },
          });
        } else if (query.includes("deleteLinkedBranch(")) {
          branchLinks = branchLinks.filter((link) => link.id !== variables.input.linkedBranchId);
        } else if (query.includes("addCloseIssueReferences(")) {
          manualPrLinks.push({ id: "PR_99" });
        } else if (query.includes("removeCloseIssueReferences(")) {
          manualPrLinks = [];
        }
      }
      return {};
    },
  };
  return {
    client,
    issues,
    lists,
    parents,
    writes,
    queries,
    setExistingBranch: (value) => {
      existingBranch = value;
    },
    branchExists: () => existingBranch,
    setKeywordPrLinks: (value) => {
      keywordPrLinks = value;
    },
  };
}

test("legacy inputs retain relates-to and Monorepo defaults", () => {
  assert.equal(parseInputs(defaults).relationship, "relates-to");
  assert.equal(parseInputs(defaults).sourceRepository, repo);
});

test("validate allowlist, integer bounds, directions and incompatible inputs", () => {
  for (const env of [
    { OPERATION: "delete" },
    { RELATIONSHIP: "security-alert" },
    { RELATED_REPOSITORY: "evil/Teck.Monorepo" },
    { SOURCE_REPOSITORY: "other/Teck.Monorepo" },
    { ISSUE_NUMBER: "0" },
    { ISSUE_NUMBER: "1;echo secret" },
    { ISSUE_NUMBER: "9007199254740993" },
    { RELATED_ISSUE_NUMBER: "" },
    { RELATED_ISSUE_NUMBER: "1" },
    { BRANCH_NAME: "feature/test" },
    { BASE_BRANCH: "main" },
  ])
    assert.throws(() => parseInputs({ ...defaults, ...env }));
  assert.equal(
    input("blocking", "add", { SOURCE_REPOSITORY: "teck-lab/teck.terraform" }).sourceRepository,
    "Teck-Lab/Teck.Terraform",
  );
});

test("branch validation rejects invalid Git refs without shell execution", () => {
  for (const name of [
    "",
    "@",
    "-option",
    "refs/heads/test",
    "a..b",
    "a@{b",
    "a b",
    "a\\b",
    ".hidden",
    "a/.b",
    "a.lock",
    "a//b",
    "a/",
    "a.",
    "a\nb",
    "a:b",
    "a*b",
    "a[b",
  ]) {
    assert.throws(() => validateBranch(name));
  }
  assert.equal(validateBranch("feature/812-fix-ci"), "feature/812-fix-ci");
  assert.throws(() => input("branch", "add", { BRANCH_NAME: "test" }), /related issue number/);
  assert.throws(
    () =>
      input("branch", "remove", {
        RELATED_ISSUE_NUMBER: "",
        BRANCH_NAME: "test",
        BASE_BRANCH: "main",
      }),
    /Base branch/,
  );
});

for (const relationship of ["relates-to", "blocked-by", "blocking", "sub-issue", "parent"]) {
  test(`${relationship}: add, read-back, repeated add, remove, repeated remove`, async () => {
    const f = fakeClient();
    const added = await applyRelationship(input(relationship), f.client);
    assert.equal(added.verifiedState, "present");
    assert.equal(f.writes.length, 1);
    const write = f.writes[0];
    const reverse = ["blocking", "parent"].includes(relationship);
    assert.ok(write.endpoint.startsWith(path(reverse ? 2 : 1)));
    if (["parent", "sub-issue"].includes(relationship)) {
      assert.deepEqual(write.body, { sub_issue_id: reverse ? 1 : 2, replace_parent: false });
    } else assert.deepEqual(write.body, { issue_id: reverse ? 1 : 2 });
    await applyRelationship(input(relationship), f.client);
    assert.equal(f.writes.length, 1);
    assert.equal(
      (await applyRelationship(input(relationship, "remove"), f.client)).verifiedState,
      "absent",
    );
    assert.equal(f.writes.length, 2);
    await applyRelationship(input(relationship, "remove"), f.client);
    assert.equal(f.writes.length, 2);
  });
}

test("cross-repository parent uses global issue ID, not the issue number", async () => {
  const f = fakeClient();
  await applyRelationship(
    input("sub-issue", "add", { RELATED_REPOSITORY: "Teck.GitOps" }),
    f.client,
  );
  assert.deepEqual(f.writes[0].body, { sub_issue_id: 20, replace_parent: false });
});

for (const relationship of ["relates-to", "blocked-by", "sub-issue"]) {
  test(`${relationship}: no success without matching read-back`, async () => {
    const f = fakeClient({ ignoreWrites: true });
    await assert.rejects(applyRelationship(input(relationship), f.client), /read-back/);
  });
}

test("rejects PRs used as source or related issue before writing", async () => {
  for (const number of [1, 2]) {
    const f = fakeClient();
    f.issues.get(path(number)).pull_request = {};
    await assert.rejects(applyRelationship(input("relates-to"), f.client), /not a pull request/);
    assert.equal(f.writes.length, 0);
  }
});

test("dependency cycles are rejected, including transitive cross-repo cycles", async () => {
  const f = fakeClient();
  f.lists.set(`${path(2)}/dependencies/blocked_by`, [issue(2, otherRepo, 20)]);
  f.lists.set(`${path(2, otherRepo)}/dependencies/blocked_by`, [issue(1)]);
  await assert.rejects(applyRelationship(input("blocked-by"), f.client), /cycle/);
  assert.equal(f.writes.length, 0);
});

test("hierarchy cycles and implicit reparenting are rejected", async () => {
  const f = fakeClient();
  f.parents.set(path(1), issue(2));
  await assert.rejects(applyRelationship(input("sub-issue"), f.client), /cycle/);
  assert.equal(f.writes.length, 0);
  f.parents.clear();
  f.parents.set(path(2), issue(3));
  await assert.rejects(applyRelationship(input("sub-issue"), f.client), /different parent/);
  assert.equal(f.writes.length, 0);
});

test("removing an absent parent does not detach a different parent", async () => {
  const f = fakeClient();
  f.parents.set(path(2), issue(3));
  await applyRelationship(input("sub-issue", "remove"), f.client);
  assert.equal(f.writes.length, 0);
  assert.equal(f.parents.get(path(2)).id, 3);
});

test("cycle traversal refuses unapproved repositories and excessive graphs", async () => {
  const f = fakeClient();
  f.lists.set(`${path(2)}/dependencies/blocked_by`, [issue(5, "other/secret")]);
  await assert.rejects(applyRelationship(input("blocked-by"), f.client), /approved/);
  assert.equal(f.writes.length, 0);
  f.lists.clear();
  for (let number = 2; number < 204; number++) {
    f.lists.set(`${path(number)}/dependencies/blocked_by`, [issue(number + 1)]);
  }
  await assert.rejects(applyRelationship(input("blocked-by"), f.client), /safety limit/);
  assert.equal(f.writes.length, 0);
});

const branchInput = (operation = "add", env = {}) =>
  input("branch", operation, {
    RELATED_REPOSITORY: "Teck.GitOps",
    RELATED_ISSUE_NUMBER: "",
    BRANCH_NAME: "feature/test",
    ...env,
  });

test("creates a native linked branch from default base and retries without duplicates", async () => {
  const f = fakeClient();
  assert.equal((await applyRelationship(branchInput(), f.client)).verifiedState, "present");
  assert.deepEqual(f.writes[0].variables.input, {
    issueId: "I_1",
    repositoryId: "R_1",
    name: "feature/test",
    oid: "a".repeat(40),
  });
  await applyRelationship(branchInput(), f.client);
  assert.equal(f.writes.length, 1);
  await applyRelationship(branchInput("remove"), f.client);
  assert.ok(f.writes[1].query.includes("deleteLinkedBranch("));
  assert.equal(f.branchExists(), true);
  await applyRelationship(branchInput("remove"), f.client);
  assert.equal(f.writes.length, 2);
});

test("existing unlinked branch is never overwritten or recreated", async () => {
  const f = fakeClient();
  f.setExistingBranch(true);
  await assert.rejects(applyRelationship(branchInput(), f.client), /already exists/);
  assert.equal(f.writes.length, 0);
});

test("branch creation must survive read-back", async () => {
  await assert.rejects(
    applyRelationship(branchInput(), fakeClient({ ignoreWrites: true }).client),
    /read-back/,
  );
});

test("explicit branch base is resolved without falling back to main", async () => {
  const f = fakeClient();
  const request = f.client.request;
  let sawBase = false;
  f.client.request = async (method, endpoint, ...args) => {
    if (endpoint.endsWith("/git/ref/heads/release%2Fnext")) {
      sawBase = true;
      return { object: { sha: "b".repeat(40), type: "commit" } };
    }
    return request(method, endpoint, ...args);
  };
  await applyRelationship(branchInput("add", { BASE_BRANCH: "release/next" }), f.client);
  assert.equal(sawBase, true);
  assert.equal(f.writes[0].variables.input.oid, "b".repeat(40));
});

test("invalid or inaccessible branch base does not create a branch", async () => {
  const f = fakeClient();
  const request = f.client.request;
  f.client.request = async (method, endpoint, ...args) =>
    endpoint.endsWith("/git/ref/heads/main")
      ? { object: { sha: "a".repeat(40), type: "tag" } }
      : request(method, endpoint, ...args);
  await assert.rejects(applyRelationship(branchInput(), f.client), /resolve to a commit/);
  assert.equal(f.writes.length, 0);
});

test("GraphQL pagination finds existing links on later pages without a mutation", async () => {
  const f = fakeClient();
  const cursors = [];
  f.client.graphql = async (query, variables) => {
    assert.ok(query.startsWith("query"));
    cursors.push(variables.after);
    return {
      node: {
        linkedBranches: {
          nodes: variables.after
            ? [
                {
                  id: "LB_2",
                  ref: { name: "feature/test", repository: { nameWithOwner: otherRepo } },
                },
              ]
            : [],
          pageInfo: { hasNextPage: !variables.after, endCursor: "page2" },
        },
      },
    };
  };
  assert.equal((await applyRelationship(branchInput(), f.client)).verifiedState, "present");
  assert.deepEqual(cursors, [null, "page2", null, "page2"]);
  assert.equal(f.writes.length, 0);
});

test("incomplete GraphQL connection or repeating cursor fails without mutations", async () => {
  for (const missing of [false, true]) {
    const f = fakeClient();
    f.client.graphql = async () =>
      missing
        ? { node: null }
        : {
            node: {
              linkedBranches: {
                nodes: [],
                pageInfo: { hasNextPage: true, endCursor: "same" },
              },
            },
          };
    await assert.rejects(
      applyRelationship(branchInput(), f.client),
      /connection|pagination cursor/,
    );
    assert.equal(f.writes.length, 0);
  }
});

test("native PR closing links add/remove idempotently without editing PR body", async () => {
  const f = fakeClient();
  assert.equal((await applyRelationship(input("pull-request"), f.client)).verifiedState, "present");
  assert.ok(f.writes[0].query.includes("addCloseIssueReferences("));
  assert.deepEqual(f.writes[0].variables.input, { issueId: "I_1", pullRequestIds: ["PR_99"] });
  await applyRelationship(input("pull-request"), f.client);
  assert.equal(f.writes.length, 1);
  await applyRelationship(input("pull-request", "remove"), f.client);
  assert.ok(f.writes[1].query.includes("removeCloseIssueReferences("));
  await applyRelationship(input("pull-request", "remove"), f.client);
  assert.equal(f.writes.length, 2);
  assert.ok(
    f.queries.some((call) => call.query.includes("includeClosedPrs: true, userLinkedOnly: true")),
  );
});

test("keyword-derived PR link is not falsely reported as completely unlinked", async () => {
  const f = fakeClient();
  f.setKeywordPrLinks([{ id: "PR_99" }]);
  await applyRelationship(input("pull-request"), f.client);
  await assert.rejects(
    applyRelationship(input("pull-request", "remove"), f.client),
    /closing keyword/,
  );
});

test("PR mutation must survive read-back", async () => {
  await assert.rejects(
    applyRelationship(input("pull-request"), fakeClient({ ignoreWrites: true }).client),
    /read-back/,
  );
});

test("HTTP adapter sends App auth, JSON IDs, API version and refuses redirects", async () => {
  const requests = [];
  const client = createClient("fake-test-token", async (url, options) => {
    requests.push({ url, options });
    return new Response("{}", { status: 201 });
  });
  await client.request("POST", `${path(1)}/dependencies/blocked_by`, { issue_id: 2 });
  assert.equal(requests[0].url, `https://api.github.com/${path(1)}/dependencies/blocked_by`);
  assert.equal(requests[0].options.headers.authorization, "Bearer fake-test-token");
  assert.equal(requests[0].options.headers["x-github-api-version"], "2026-03-10");
  assert.equal(requests[0].options.redirect, "error");
  assert.deepEqual(JSON.parse(requests[0].options.body), { issue_id: 2 });
  await assert.rejects(client.request("GET", "https://evil.example"), /allowlist/);
  assert.equal(requests.length, 1);
});

test("HTTP errors, GraphQL partial errors and transport errors do not leak response secrets", async () => {
  const secret = "SENSITIVE_TEST_MARKER";
  for (const status of [401, 403, 404, 422, 429, 503]) {
    const client = createClient("fake-test-token", async () => new Response(secret, { status }));
    await assert.rejects(
      client.request("GET", path(1)),
      (error) => !error.message.includes(secret) && error.message.includes(`HTTP ${status}`),
    );
  }
  const client = createClient("fake-test-token", async () =>
    Response.json({ errors: [{ message: secret }], data: { issue: {} } }),
  );
  await assert.rejects(
    client.graphql("query { viewer { login } }", {}),
    (error) => !error.message.includes(secret) && /GraphQL failed/.test(error.message),
  );
  const transport = createClient("fake-test-token", async () => {
    throw new Error(secret);
  });
  await assert.rejects(
    transport.request("GET", path(1)),
    (error) => !error.message.includes(secret),
  );
});

test("HTTP pagination includes later pages and fails closed at the limit", async () => {
  const urls = [];
  const client = createClient("fake-test-token", async (url) => {
    urls.push(url);
    return Response.json(
      url.endsWith("page=1") ? Array.from({ length: 100 }, (_, id) => ({ id })) : [{ id: 999 }],
    );
  });
  assert.equal((await client.list(`${path(1)}/relates_to`)).at(-1).id, 999);
  assert.equal(urls.length, 2);
  const unlimited = createClient("fake-test-token", async () =>
    Response.json(Array.from({ length: 100 }, (_, id) => ({ id }))),
  );
  await assert.rejects(unlimited.list(`${path(1)}/relates_to`), /pagination/);
});

test("workflow stays explicit, uses App minting, validates first and queues writes", async () => {
  const workflow = await readFile(
    new URL("../workflows/paperclip-issue-relationships.yml", import.meta.url),
    "utf8",
  );
  assert.match(workflow, /workflow_dispatch:/);
  assert.doesNotMatch(workflow, /^ {2}(issues|schedule|pull_request|push):/m);
  assert.match(workflow, /create-github-app-token@[a-f0-9]{40}/);
  assert.match(workflow, /persist-credentials: false/);
  assert.match(workflow, /ref: \$\{\{ github.event.repository.default_branch \}\}/);
  assert.match(workflow, /queue: max/);
  assert.ok(workflow.indexOf("--validate") < workflow.indexOf("create-github-app-token@"));
  assert.match(workflow, /permission-contents:.*relationship == 'branch'.*operation == 'add'/);
  assert.match(workflow, /permission-pull-requests:.*relationship == 'pull-request'/);
});

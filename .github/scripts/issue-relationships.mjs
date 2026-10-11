import { appendFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";

const REPOSITORIES = ["Teck.Monorepo", "Teck.Terraform", "Teck.GitOps"];
const RELATIONSHIPS = [
  "relates-to",
  "blocked-by",
  "blocking",
  "sub-issue",
  "parent",
  "branch",
  "pull-request",
];
const MAX_PAGES = 10;
const MAX_GRAPH_NODES = 200;

function repository(value) {
  const name = value?.replace(/^Teck-Lab\//i, "");
  const canonical = REPOSITORIES.find((item) => item.toLowerCase() === name?.toLowerCase());
  if (!canonical)
    throw new Error("Repository must be one of the three approved Teck-Lab repositories.");
  return `Teck-Lab/${canonical}`;
}

function positiveInteger(value, label) {
  if (!/^[1-9][0-9]*$/.test(String(value)) || !Number.isSafeInteger(Number(value))) {
    throw new Error(`${label} must be a positive safe integer.`);
  }
  return Number(value);
}

export function validateBranch(value) {
  if (
    typeof value !== "string" ||
    !value ||
    value === "@" ||
    value.startsWith("-") ||
    value.startsWith("refs/") ||
    /[\s~^:?*[\\]/.test(value) ||
    [...value].some(
      (character) => character.charCodeAt(0) < 32 || character.charCodeAt(0) === 127,
    ) ||
    value.includes("..") ||
    value.includes("@{") ||
    value.endsWith(".") ||
    value.split("/").some((part) => !part || part.startsWith(".") || part.endsWith(".lock"))
  ) {
    throw new Error("Branch must be a valid short Git branch name (without refs/heads/).");
  }
  return value;
}

export function parseInputs(env) {
  const relationship = env.RELATIONSHIP || "relates-to";
  if (!RELATIONSHIPS.includes(relationship)) {
    throw new Error(
      "Unsupported relationship. Native security-alert linking has no documented public API in this bridge.",
    );
  }
  if (!["add", "remove"].includes(env.OPERATION))
    throw new Error("Operation must be add or remove.");
  const input = {
    operation: env.OPERATION,
    relationship,
    sourceRepository: repository(env.SOURCE_REPOSITORY || "Teck.Monorepo"),
    issueNumber: positiveInteger(env.ISSUE_NUMBER, "Source issue number"),
    relatedRepository: repository(env.RELATED_REPOSITORY),
  };
  if (relationship === "branch") {
    if (env.RELATED_ISSUE_NUMBER)
      throw new Error("Do not provide a related issue number for a branch.");
    input.branchName = validateBranch(env.BRANCH_NAME);
    if (env.BASE_BRANCH) {
      if (input.operation !== "add")
        throw new Error("Base branch is only used when creating a linked branch.");
      input.baseBranch = validateBranch(env.BASE_BRANCH);
    }
  } else {
    if (env.BRANCH_NAME || env.BASE_BRANCH)
      throw new Error("Branch inputs require the branch relationship.");
    input.relatedIssueNumber = positiveInteger(
      env.RELATED_ISSUE_NUMBER,
      "Related issue or PR number",
    );
    if (
      input.sourceRepository === input.relatedRepository &&
      input.issueNumber === input.relatedIssueNumber
    ) {
      throw new Error("An issue cannot link to itself.");
    }
  }
  return input;
}

// Never include response bodies, issue bodies, environment values, or tokens in errors/logs.
export function createClient(token, fetchImpl = fetch) {
  if (!token) throw new Error("GitHub App token is required.");
  async function request(method, path, body, allowMissing = false) {
    if (
      path !== "graphql" &&
      !/^repos\/Teck-Lab\/Teck\.(Monorepo|Terraform|GitOps)(\/|$)/.test(path)
    ) {
      throw new Error("API request is outside the repository allowlist.");
    }
    let response;
    try {
      response = await fetchImpl(`https://api.github.com/${path}`, {
        method,
        redirect: "error",
        signal: AbortSignal.timeout(20_000),
        headers: {
          authorization: `Bearer ${token}`,
          accept: "application/vnd.github+json",
          "content-type": "application/json",
          "x-github-api-version": "2026-03-10",
          "user-agent": "teck-issue-relationships",
        },
        ...(body === undefined ? {} : { body: JSON.stringify(body) }),
      });
    } catch {
      throw new Error(
        "GitHub request failed or timed out; no successful relationship change is claimed.",
      );
    }
    if (allowMissing && response.status === 404) return null;
    if (!response.ok)
      throw new Error(
        `GitHub API returned HTTP ${response.status}; check App permissions, repository access and API availability.`,
      );
    if (response.status === 204) return null;
    try {
      return await response.json();
    } catch {
      throw new Error("GitHub returned an invalid JSON response.");
    }
  }
  return {
    request,
    async list(path) {
      const items = [];
      for (let page = 1; page <= MAX_PAGES; page++) {
        const data = await request("GET", `${path}?per_page=100&page=${page}`);
        if (!Array.isArray(data)) throw new Error("Expected a GitHub relationship list.");
        items.push(...data);
        if (data.length < 100) return items;
      }
      throw new Error(
        "Relationship pagination exceeded the safety limit; no incomplete list is used.",
      );
    },
    async graphql(query, variables) {
      const result = await request("POST", "graphql", { query, variables });
      if (result?.errors?.length || !result?.data)
        throw new Error("GitHub GraphQL failed; check App permissions and API capability.");
      return result.data;
    },
  };
}

function issuePath(repo, number) {
  return `repos/${repo}/issues/${number}`;
}
function pathFromIssue(issue) {
  const match = /^https:\/\/api\.github\.com\/repos\/([^/]+\/[^/]+)\/issues\/([1-9][0-9]*)$/.exec(
    issue?.url,
  );
  if (!match) throw new Error("GitHub returned an invalid issue identity.");
  return issuePath(repository(match[1]), positiveInteger(match[2], "Issue number"));
}
function assertIssue(issue) {
  if (!Number.isSafeInteger(issue?.id) || issue.id < 1 || !issue.node_id || issue.pull_request) {
    throw new Error("Expected an issue, not a pull request, with valid GitHub IDs.");
  }
  return issue;
}

async function rejectCycle(client, start, forbidden, parents) {
  const pending = [start],
    visited = new Set();
  while (pending.length) {
    const current = pending.pop();
    if (current.id === forbidden.id)
      throw new Error("Requested relationship would create a cycle.");
    if (visited.has(current.id)) {
      if (parents) throw new Error("Existing parent graph contains a cycle.");
      continue;
    }
    visited.add(current.id);
    if (visited.size > MAX_GRAPH_NODES)
      throw new Error("Cycle check exceeded the safety limit; no write performed.");
    const path = pathFromIssue(current);
    if (parents) {
      const parent = await client.request("GET", `${path}/parent`, undefined, true);
      if (parent) pending.push(assertIssue(parent));
    } else {
      pending.push(...(await client.list(`${path}/dependencies/blocked_by`)).map(assertIssue));
    }
  }
}

async function connection(client, issueId, field, selection, args = "") {
  const nodes = [];
  let after = null;
  for (let page = 0; page < MAX_PAGES; page++) {
    const data = await client.graphql(
      `query($id: ID!, $after: String) {
      node(id: $id) { ... on Issue { ${field}(first: 100, after: $after ${args}) {
        nodes { ${selection} } pageInfo { hasNextPage endCursor }
      } } }
    }`,
      { id: issueId, after },
    );
    const result = data.node?.[field];
    if (!result || !Array.isArray(result.nodes) || !result.pageInfo)
      throw new Error("GitHub did not return the requested issue connection.");
    nodes.push(...result.nodes);
    if (!result.pageInfo.hasNextPage) return nodes;
    if (!result.pageInfo.endCursor || result.pageInfo.endCursor === after)
      throw new Error("Invalid GraphQL pagination cursor.");
    after = result.pageInfo.endCursor;
  }
  throw new Error("GraphQL pagination exceeded the safety limit.");
}

async function linkedBranch(client, input, source) {
  const read = () =>
    connection(
      client,
      source.node_id,
      "linkedBranches",
      "id ref { name repository { nameWithOwner } }",
    );
  const match = (link) =>
    link?.ref?.name === input.branchName &&
    link.ref.repository?.nameWithOwner?.toLowerCase() === input.relatedRepository.toLowerCase();
  const links = (await read()).filter(match);
  if (input.operation === "add" && !links.length) {
    const repoPath = `repos/${input.relatedRepository}`;
    const existing = await client.request(
      "GET",
      `${repoPath}/git/ref/heads/${encodeURIComponent(input.branchName)}`,
      undefined,
      true,
    );
    if (existing)
      throw new Error(
        "Branch already exists but is not linked. This API only creates new linked branches; link the existing branch in GitHub UI.",
      );
    const repo = await client.request("GET", repoPath);
    if (!repo?.node_id) throw new Error("GitHub repository node ID is missing.");
    const base = validateBranch(input.baseBranch || repo.default_branch);
    const ref = await client.request(
      "GET",
      `${repoPath}/git/ref/heads/${encodeURIComponent(base)}`,
    );
    if (!/^[a-f0-9]{40}$/.test(ref?.object?.sha) || ref.object.type !== "commit")
      throw new Error("Base branch did not resolve to a commit.");
    await client.graphql(
      `mutation($input: CreateLinkedBranchInput!) {
      createLinkedBranch(input: $input) { linkedBranch { id } }
    }`,
      {
        input: {
          issueId: source.node_id,
          repositoryId: repo.node_id,
          name: input.branchName,
          oid: ref.object.sha,
        },
      },
    );
  } else if (input.operation === "remove") {
    for (const link of links) {
      await client.graphql(
        `mutation($input: DeleteLinkedBranchInput!) {
        deleteLinkedBranch(input: $input) { issue { id } }
      }`,
        { input: { linkedBranchId: link.id } },
      );
    }
  }
  return (await read()).some(match);
}

async function linkedPullRequest(client, input, source) {
  const pr = await client.request(
    "GET",
    `repos/${input.relatedRepository}/pulls/${input.relatedIssueNumber}`,
  );
  if (!pr?.node_id) throw new Error("GitHub pull request node ID is missing.");
  // Only manual links are managed, not PR-body-derived closing keywords.
  const read = (manual) =>
    connection(
      client,
      source.node_id,
      "closedByPullRequestsReferences",
      "id",
      `, includeClosedPrs: true, userLinkedOnly: ${manual}`,
    );
  const present = (await read(true)).some((item) => item?.id === pr.node_id);
  if (present !== (input.operation === "add")) {
    const name =
      input.operation === "add" ? "addCloseIssueReferences" : "removeCloseIssueReferences";
    const type =
      input.operation === "add"
        ? "AddCloseIssueReferencesInput"
        : "RemoveCloseIssueReferencesInput";
    await client.graphql(`mutation($input: ${type}!) { ${name}(input: $input) { issue { id } } }`, {
      input: { issueId: source.node_id, pullRequestIds: [pr.node_id] },
    });
  }
  const verified = (await read(true)).some((item) => item?.id === pr.node_id);
  if (
    input.operation === "remove" &&
    !verified &&
    (await read(false)).some((item) => item?.id === pr.node_id)
  ) {
    throw new Error(
      "Manual PR link removed, but a closing keyword still links this PR. Update its body through MCP before claiming complete unlinking.",
    );
  }
  return verified;
}

export async function applyRelationship(input, client) {
  const sourcePath = issuePath(input.sourceRepository, input.issueNumber);
  const source = assertIssue(await client.request("GET", sourcePath));
  let present;
  if (input.relationship === "branch") {
    present = await linkedBranch(client, input, source);
  } else if (input.relationship === "pull-request") {
    present = await linkedPullRequest(client, input, source);
  } else {
    const targetPath = issuePath(input.relatedRepository, input.relatedIssueNumber);
    const target = assertIssue(await client.request("GET", targetPath));
    const reversed = ["blocking", "parent"].includes(input.relationship);
    const left = reversed ? target : source,
      right = reversed ? source : target;
    const path = reversed ? targetPath : sourcePath;
    if (["sub-issue", "parent"].includes(input.relationship)) {
      const childPath = reversed ? sourcePath : targetPath;
      const read = () => client.request("GET", `${childPath}/parent`, undefined, true);
      const parent = await read();
      present = parent?.id === left.id;
      if (input.operation === "add" && !present) {
        if (parent)
          throw new Error(
            "Sub-issue already has a different parent. Remove that relationship explicitly before reparenting.",
          );
        await rejectCycle(client, left, right, true);
        await client.request("POST", `${path}/sub_issues`, {
          sub_issue_id: right.id,
          replace_parent: false,
        });
      } else if (input.operation === "remove" && present) {
        await client.request("DELETE", `${path}/sub_issue`, { sub_issue_id: right.id });
      }
      present = (await read())?.id === left.id;
      const children = await client.list(`${path}/sub_issues`);
      if (children.some((child) => child.id === right.id) !== present)
        throw new Error("Parent and sub-issue read-back disagree.");
    } else {
      const suffix = input.relationship === "relates-to" ? "relates_to" : "dependencies/blocked_by";
      const read = () => client.list(`${path}/${suffix}`);
      present = (await read()).some((item) => item.id === right.id);
      if (input.operation === "add" && !present) {
        if (suffix !== "relates_to") await rejectCycle(client, right, left, false);
        await client.request("POST", `${path}/${suffix}`, { issue_id: right.id });
      } else if (input.operation === "remove" && present) {
        await client.request("DELETE", `${path}/${suffix}/${right.id}`);
      }
      present = (await read()).some((item) => item.id === right.id);
    }
  }
  if (present !== (input.operation === "add"))
    throw new Error(
      "Relationship read-back did not match the requested state; no success is claimed.",
    );
  return { ...input, verifiedState: present ? "present" : "absent" };
}

async function main() {
  const input = parseInputs(process.env);
  if (process.argv.includes("--validate")) return;
  const result = await applyRelationship(input, createClient(process.env.GH_TOKEN));
  const summary =
    `### GitHub issue relationship\n\n` +
    `- Operation: ${result.operation}\n- Relationship: ${result.relationship}\n` +
    `- Source: ${result.sourceRepository}#${result.issueNumber}\n` +
    `- Related: ${result.relatedRepository}${result.branchName ? ` branch ${JSON.stringify(result.branchName)}` : `#${result.relatedIssueNumber}`}\n` +
    `- Verified state: ${result.verifiedState}\n`;
  if (process.env.GITHUB_STEP_SUMMARY) await appendFile(process.env.GITHUB_STEP_SUMMARY, summary);
  console.log("Requested native relationship verified successfully.");
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main().catch((error) => {
    console.error(error.message);
    process.exitCode = 1;
  });
}

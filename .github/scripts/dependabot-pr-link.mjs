import { readFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";
import { GitHubClient } from "./security-alert-intake.mjs";

const configUrl = new URL("../security-intake.json", import.meta.url);
const linkMarker = "<!-- teck-dependabot-security-links -->";

export function parseNames(value = "") {
  return new Set(
    value
      .split(",")
      .map((name) => name.trim().toLowerCase())
      .filter(Boolean),
  );
}

export function dependabotIssueMetadata(body = "") {
  return {
    advisory: body.match(/^- Advisory:\s*(\S+)/m)?.[1]?.toLowerCase() ?? null,
    package: body.match(/^- Package:\s*`([^`]+)`/m)?.[1]?.toLowerCase() ?? null,
  };
}

export function matchingIssues(issues, ghsaId, dependencyNames) {
  const advisory = ghsaId.trim().toLowerCase();
  const names = parseNames(dependencyNames);
  if (!advisory) return [];
  return issues.filter((issue) => {
    const metadata = dependabotIssueMetadata(issue.body ?? "");
    return (
      metadata.advisory === advisory &&
      (!metadata.package || names.size === 0 || names.has(metadata.package))
    );
  });
}

export function linkedPullRequestBody(body, issues) {
  const links = issues.map((issue) => `Closes #${issue.number}`).join("\n");
  const block = `${linkMarker}\n${links}`;
  if ((body ?? "").includes(linkMarker)) {
    return body.replace(new RegExp(`${linkMarker}(?:\\nCloses #[0-9]+)*`), block);
  }
  return `${body ?? ""}${body ? "\n\n" : ""}${block}`;
}

async function projectContext(client, config) {
  const query =
    "query($org:String!,$number:Int!){organization(login:$org){projectV2(number:$number){id fields(first:50){nodes{... on ProjectV2FieldCommon{id name dataType} ... on ProjectV2SingleSelectField{options{id name}}}}}}}";
  const data = await client.graphql(query, {
    org: config.project.organization,
    number: config.project.number,
  });
  const project = data.organization?.projectV2;
  if (!project)
    throw new Error(
      `Project ${config.project.organization}#${config.project.number} was not found`,
    );
  return project;
}

async function addPullRequestToProject(client, project, pullRequest, config, security) {
  const add = await client.graphql(
    "mutation($project:ID!,$content:ID!){addProjectV2ItemById(input:{projectId:$project,contentId:$content}){item{id}}}",
    { project: project.id, content: pullRequest.node_id },
  );
  const itemId = add.addProjectV2ItemById.item.id;
  const desired = {
    [config.project.fields.status]: security ? "In review" : "Ready",
    ...(security ? { [config.project.fields.workType]: "Security" } : {}),
  };
  for (const field of project.fields.nodes) {
    const value = desired[field.name];
    if (value === undefined) continue;
    const option = field.options?.find(
      (candidate) => candidate.name.toLowerCase() === value.toLowerCase(),
    );
    if (option)
      await client.graphql(
        "mutation($project:ID!,$item:ID!,$field:ID!,$option:String!){updateProjectV2ItemFieldValue(input:{projectId:$project,itemId:$item,fieldId:$field,value:{singleSelectOptionId:$option}}){projectV2Item{id}}}",
        { project: project.id, item: itemId, field: field.id, option: option.id },
      );
  }
}

export async function run() {
  const token = process.env.GITHUB_TOKEN;
  const [owner, repo] = (process.env.GITHUB_REPOSITORY ?? "").split("/");
  const pullNumber = Number(process.env.PR_NUMBER);
  const ghsaId = process.env.GHSA_ID ?? "";
  if (!token || !owner || !repo || !pullNumber)
    throw new Error("GITHUB_TOKEN, GITHUB_REPOSITORY, and PR_NUMBER are required");
  const config = JSON.parse(await readFile(configUrl, "utf8"));
  const client = new GitHubClient(token);
  const [pullResponse, issueResponse] = await Promise.all([
    client.request(`/repos/${owner}/${repo}/pulls/${pullNumber}`),
    client.paginate(
      `/repos/${owner}/${repo}/issues?state=open&labels=${encodeURIComponent("security:tracked,source:dependabot")}&per_page=100`,
    ),
  ]);
  const pullRequest = pullResponse.data;
  if (pullRequest.user?.login !== "dependabot[bot]")
    throw new Error(`PR #${pullNumber} is not authored by Dependabot`);
  const project = await projectContext(client, config);
  await addPullRequestToProject(client, project, pullRequest, config, Boolean(ghsaId));
  if (!ghsaId) {
    console.log(
      `Added version-update Dependabot PR #${pullNumber} to Teck Scrum; no security issue link is required.`,
    );
    return;
  }
  const issues = matchingIssues(
    issueResponse.data.filter((issue) => !issue.pull_request),
    ghsaId,
    process.env.DEPENDENCY_NAMES ?? "",
  );
  if (issues.length === 0) {
    console.log(
      `No tracked Dependabot issue matches ${ghsaId}; the scheduled intake will create it before the next reconciliation.`,
    );
    return;
  }

  await client.request(`/repos/${owner}/${repo}/pulls/${pullNumber}`, {
    method: "PATCH",
    body: JSON.stringify({ body: linkedPullRequestBody(pullRequest.body ?? "", issues) }),
  });
  for (const issue of issues) {
    if (!issue.labels.some((label) => label.name === "agent:in-review")) {
      await client.request(`/repos/${owner}/${repo}/issues/${issue.number}/labels`, {
        method: "POST",
        body: JSON.stringify({ labels: ["agent:in-review"] }),
      });
    }
  }
  console.log(
    `Linked Dependabot PR #${pullNumber} to ${issues.map((issue) => `#${issue.number}`).join(", ")} and added it to Teck Scrum.`,
  );
}

const invokedPath = process.argv[1] ? pathToFileURL(process.argv[1]).href : null;
if (invokedPath === import.meta.url)
  run().catch((error) => {
    console.error(error);
    process.exitCode = 1;
  });

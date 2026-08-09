import { pathToFileURL } from "node:url";
import { GitHubClient } from "./security-alert-intake.mjs";

const markerPattern = /<!-- teck-dependabot-stuck: ([^\s]+) -->/;
const prLinkMarker = "<!-- teck-dependabot-stuck-issue -->";
const failedConclusions = new Set(["action_required", "failure", "startup_failure", "timed_out"]);
const ignoredChecks = new Set(["Link alert issue and Project item"]);

export function fingerprint(owner, repo, pullNumber) {
  return `${owner}/${repo}#${pullNumber}`.toLowerCase();
}

export function fingerprintFromBody(body = "") {
  return body.match(markerPattern)?.[1]?.toLowerCase() ?? null;
}

export function checkState(checkRuns, statuses, now = Date.now(), graceMinutes = 20) {
  checkRuns = checkRuns.filter((check) => !ignoredChecks.has(check.name));
  if (checkRuns.length === 0 && statuses.length === 0) return { state: "pending", failures: [] };
  const pendingChecks = checkRuns.filter((check) => check.status !== "completed");
  const pendingStatuses = statuses.filter((status) => status.state === "pending");
  const failures = [
    ...checkRuns
      .filter((check) => check.status === "completed" && failedConclusions.has(check.conclusion))
      .map((check) => ({ name: check.name, url: check.html_url, completedAt: check.completed_at })),
    ...statuses
      .filter((status) => ["error", "failure"].includes(status.state))
      .map((status) => ({ name: status.context, url: status.target_url, completedAt: status.updated_at })),
  ];
  if (pendingChecks.length || pendingStatuses.length) return { state: "pending", failures };
  if (!failures.length) return { state: "healthy", failures: [] };
  const newestFailure = Math.max(...failures.map((failure) => Date.parse(failure.completedAt ?? 0)).filter(Number.isFinite));
  const oldEnough = Number.isFinite(newestFailure) && now - newestFailure >= graceMinutes * 60_000;
  return { state: oldEnough ? "stuck" : "young-failure", failures };
}

export function issueBody(owner, repo, pull, failures) {
  const key = fingerprint(owner, repo, pull.number);
  return [
    `<!-- teck-dependabot-stuck: ${key} -->`,
    "## Stuck Dependabot pull request",
    "",
    `Dependabot updated this branch, but its CI checks remain failed after the grace period. Repair the existing branch and PR; do not open a replacement PR.`,
    "",
    `- Pull request: ${pull.html_url}`,
    `- Branch: \`${pull.head.ref}\``,
    `- Head commit: \`${pull.head.sha}\``,
    "- Failed checks:",
    ...failures.map((failure) => `  - [${failure.name}](${failure.url ?? pull.html_url})`),
    "",
    "Opening this issue makes the repair eligible for Orca intake. The reconciler closes it automatically after CI recovers or the pull request closes.",
  ].join("\n");
}

export function linkedPullRequestBody(body, issueNumber) {
  const block = `${prLinkMarker}\nFixes #${issueNumber}`;
  if ((body ?? "").includes(prLinkMarker))
    return body.replace(new RegExp(`${prLinkMarker}(?:\\nFixes #[0-9]+)*`), block);
  return `${body ?? ""}${body ? "\n\n" : ""}${block}`;
}

export function indexIssues(issues) {
  return new Map(
    issues
      .filter((issue) => !issue.pull_request && fingerprintFromBody(issue.body))
      .map((issue) => [fingerprintFromBody(issue.body), issue]),
  );
}

async function ensureLabels(client, owner, repo) {
  const definitions = [
    { name: "dependabot:stuck", color: "b60205", description: "Dependabot pull request requires a manual repair" },
    { name: "ci:failed", color: "d93f0b", description: "Required CI or validation is failing" },
  ];
  for (const label of definitions) {
    const path = `/repos/${owner}/${repo}/labels/${encodeURIComponent(label.name)}`;
    const existing = await client.request(path, {}, [404]);
    if (existing.skipped)
      await client.request(`/repos/${owner}/${repo}/labels`, { method: "POST", body: JSON.stringify(label) });
    else
      await client.request(path, { method: "PATCH", body: JSON.stringify({ new_name: label.name, color: label.color, description: label.description }) });
  }
}

async function collectCheckRuns(client, owner, repo, sha) {
  const runs = [];
  for (let page = 1; ; page++) {
    const response = await client.request(`/repos/${owner}/${repo}/commits/${sha}/check-runs?per_page=100&page=${page}`);
    const batch = response.data.check_runs ?? [];
    runs.push(...batch);
    if (batch.length < 100) return runs;
  }
}

async function closeIssue(client, owner, repo, issue, reason) {
  if (!issue || issue.state === "closed") return;
  await client.request(`/repos/${owner}/${repo}/issues/${issue.number}/comments`, {
    method: "POST",
    body: JSON.stringify({ body: reason }),
  });
  await client.request(`/repos/${owner}/${repo}/issues/${issue.number}`, {
    method: "PATCH",
    body: JSON.stringify({ state: "closed", state_reason: "completed" }),
  });
}

async function upsertStuckIssue(client, owner, repo, pull, failures, existing) {
  const title = `[Dependabot] Repair failing CI for PR #${pull.number}`;
  const body = issueBody(owner, repo, pull, failures);
  if (!existing) {
    const response = await client.request(`/repos/${owner}/${repo}/issues`, {
      method: "POST",
      body: JSON.stringify({ title, body, labels: ["dependabot:stuck", "ci:failed", "source:dependabot", "agent:ready"] }),
    });
    return response.data;
  }
  const labels = existing.labels.map((label) => label.name);
  const activeLifecycle = labels.some((label) => label.startsWith("agent:") && label !== "agent:completed");
  const lifecycle = activeLifecycle ? [] : ["agent:ready"];
  const response = await client.request(`/repos/${owner}/${repo}/issues/${existing.number}`, {
    method: "PATCH",
    body: JSON.stringify({
      title,
      body,
      state: "open",
      labels: [...new Set([...labels.filter((label) => label !== "agent:completed"), "dependabot:stuck", "ci:failed", "source:dependabot", ...lifecycle])],
    }),
  });
  return response.data;
}

export async function run() {
  const token = process.env.GITHUB_TOKEN;
  const [owner, repo] = (process.env.GITHUB_REPOSITORY ?? "").split("/");
  if (!token || !owner || !repo) throw new Error("GITHUB_TOKEN and GITHUB_REPOSITORY are required");
  const client = new GitHubClient(token);
  const graceMinutes = Number(process.env.DEPENDABOT_STUCK_GRACE_MINUTES ?? 20);
  if (!Number.isFinite(graceMinutes) || graceMinutes < 1) throw new Error("DEPENDABOT_STUCK_GRACE_MINUTES must be a positive number");
  const dryRun = process.env.DEPENDABOT_STUCK_DRY_RUN === "true";
  const [pullsResponse, issuesResponse] = await Promise.all([
    client.paginate(`/repos/${owner}/${repo}/pulls?state=open&per_page=100`),
    client.paginate(`/repos/${owner}/${repo}/issues?state=all&labels=${encodeURIComponent("dependabot:stuck")}&per_page=100`),
  ]);
  const pulls = pullsResponse.data.filter((pull) => pull.user?.login === "dependabot[bot]");
  const existing = indexIssues(issuesResponse.data);
  const activeKeys = new Set(pulls.map((pull) => fingerprint(owner, repo, pull.number)));
  const summary = [];

  if (!dryRun) await ensureLabels(client, owner, repo);
  for (const pull of pulls) {
    const [checks, status] = await Promise.all([
      collectCheckRuns(client, owner, repo, pull.head.sha),
      client.request(`/repos/${owner}/${repo}/commits/${pull.head.sha}/status`),
    ]);
    const result = checkState(checks, status.data.statuses ?? [], Date.now(), graceMinutes);
    const issue = existing.get(fingerprint(owner, repo, pull.number));
    summary.push({ pull: pull.number, head: pull.head.sha, state: result.state, failures: result.failures.map((failure) => failure.name) });
    if (dryRun) continue;
    if (result.state === "stuck") {
      const tracked = await upsertStuckIssue(client, owner, repo, pull, result.failures, issue);
      await client.request(`/repos/${owner}/${repo}/pulls/${pull.number}`, {
        method: "PATCH",
        body: JSON.stringify({ body: linkedPullRequestBody(pull.body ?? "", tracked.number) }),
      });
    } else if (result.state === "healthy") {
      await closeIssue(client, owner, repo, issue, "CI is green for the current Dependabot head commit. Closing this repair issue automatically.");
    }
  }
  if (!dryRun) {
    for (const [key, issue] of existing) {
      if (!activeKeys.has(key)) await closeIssue(client, owner, repo, issue, "The associated Dependabot pull request is no longer open. Closing this repair issue automatically.");
    }
  }
  console.log(JSON.stringify({ dryRun, graceMinutes, pullRequests: summary }, null, 2));
}

const invokedPath = process.argv[1] ? pathToFileURL(process.argv[1]).href : null;
if (invokedPath === import.meta.url)
  run().catch((error) => {
    console.error(error);
    process.exitCode = 1;
  });

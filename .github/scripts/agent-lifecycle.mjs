import { readFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";

const defaultConfigPath = new URL("../agent-lifecycle.json", import.meta.url);

export function lifecycleNames(config) {
  return new Set(config.labels.map((label) => label.name));
}

export function currentLifecycle(labels, config, excludedLabel = null) {
  const names = lifecycleNames(config);
  return labels
    .map((label) => (typeof label === "string" ? label : label.name))
    .filter((name) => names.has(name) && name !== excludedLabel)
    .sort();
}

export function planIssueEvent(event, config) {
  const action = event.action;
  const labels = event.issue?.labels ?? [];
  const managedStates = currentLifecycle(labels, config);

  if (action === "closed") {
    if (managedStates.length === 0) return { operation: "noop" };
    return { operation: "transition", target: "agent:completed" };
  }

  if (action === "reopened") {
    if (managedStates.length === 0) return { operation: "noop" };
    return { operation: "transition", target: "agent:needs-input" };
  }

  if (action !== "labeled" || !event.label?.name) return { operation: "noop" };

  const target = event.label.name;
  if (!lifecycleNames(config).has(target)) return { operation: "noop" };

  const previousStates = currentLifecycle(labels, config, target);
  if (previousStates.length > 1) {
    return {
      operation: "reject",
      target,
      reason: `multiple prior lifecycle labels are active: ${previousStates.join(", ")}`,
    };
  }

  const previous = previousStates[0] ?? null;
  const allowed = config.transitions[target] ?? [];
  if (!allowed.includes(previous)) {
    return {
      operation: "reject",
      target,
      reason: `${previous ?? "unmanaged"} cannot transition to ${target}`,
    };
  }

  return { operation: "transition", target };
}

export function labelsToRemove(labels, target, config) {
  return currentLifecycle(labels, config).filter((name) => name !== target);
}

async function githubRequest(path, options = {}) {
  const token = process.env.GITHUB_TOKEN;
  if (!token) throw new Error("GITHUB_TOKEN is required");

  const response = await fetch(`https://api.github.com${path}`, {
    ...options,
    headers: {
      Accept: "application/vnd.github+json",
      Authorization: `Bearer ${token}`,
      "X-GitHub-Api-Version": "2022-11-28",
      "User-Agent": "teck-agent-lifecycle",
      ...options.headers,
    },
  });

  if (!response.ok) {
    const body = await response.text();
    throw new Error(
      `GitHub API ${options.method ?? "GET"} ${path} failed (${response.status}): ${body}`,
    );
  }

  if (response.status === 204) return null;
  return response.json();
}

async function syncLabels(owner, repo, config) {
  for (const label of config.labels) {
    const encodedName = encodeURIComponent(label.name);
    const body = JSON.stringify({
      new_name: label.name,
      color: label.color,
      description: label.description,
    });

    try {
      await githubRequest(`/repos/${owner}/${repo}/labels/${encodedName}`, {
        method: "PATCH",
        body,
      });
    } catch (error) {
      if (!error.message.includes("(404)")) throw error;
      await githubRequest(`/repos/${owner}/${repo}/labels`, {
        method: "POST",
        body: JSON.stringify(label),
      });
    }
  }
}

async function removeLabel(owner, repo, issueNumber, label) {
  try {
    await githubRequest(
      `/repos/${owner}/${repo}/issues/${issueNumber}/labels/${encodeURIComponent(label)}`,
      { method: "DELETE" },
    );
  } catch (error) {
    if (!error.message.includes("(404)")) throw error;
  }
}

async function transitionIssue(owner, repo, issue, target, config) {
  for (const label of labelsToRemove(issue.labels ?? [], target, config)) {
    await removeLabel(owner, repo, issue.number, label);
  }

  const current = currentLifecycle(issue.labels ?? [], config);
  if (!current.includes(target)) {
    await githubRequest(`/repos/${owner}/${repo}/issues/${issue.number}/labels`, {
      method: "POST",
      body: JSON.stringify({ labels: [target] }),
    });
  }
}

async function run() {
  const config = JSON.parse(await readFile(defaultConfigPath, "utf8"));
  const [owner, repo] = (process.env.GITHUB_REPOSITORY ?? "").split("/");
  if (!owner || !repo) throw new Error("GITHUB_REPOSITORY must be owner/repo");

  if (process.env.GITHUB_EVENT_NAME === "workflow_dispatch") {
    await syncLabels(owner, repo, config);
    console.log(`Synchronized ${config.labels.length} agent lifecycle labels.`);
    return;
  }

  const eventPath = process.env.GITHUB_EVENT_PATH;
  if (!eventPath) throw new Error("GITHUB_EVENT_PATH is required");
  const event = JSON.parse(await readFile(eventPath, "utf8"));
  const plan = planIssueEvent(event, config);

  if (plan.operation === "noop") return;
  if (plan.operation === "reject") {
    await removeLabel(owner, repo, event.issue.number, plan.target);
    console.log(`Rejected ${plan.target} on #${event.issue.number}: ${plan.reason}`);
    return;
  }

  await transitionIssue(owner, repo, event.issue, plan.target, config);
  console.log(`Transitioned #${event.issue.number} to ${plan.target}.`);
}

const invokedPath = process.argv[1] ? pathToFileURL(process.argv[1]).href : null;
if (invokedPath === import.meta.url) {
  run().catch((error) => {
    console.error(error);
    process.exitCode = 1;
  });
}

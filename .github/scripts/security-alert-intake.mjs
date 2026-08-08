import { readFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";

const configUrl = new URL("../security-intake.json", import.meta.url);
const markerPattern = /<!-- teck-security-fingerprint: ([^\s]+) -->/;

export function severity(value) {
  const normalized = String(value ?? "medium").toLowerCase();
  if (normalized === "error") return "high";
  if (normalized === "warning" || normalized === "moderate") return "medium";
  return ["low", "medium", "high", "critical"].includes(normalized) ? normalized : "medium";
}

export function priorityForSeverity(value) {
  return { low: "Low", medium: "Medium", high: "High", critical: "Urgent" }[severity(value)];
}

export function priorityForFinding(finding) {
  if (finding.kev || (finding.epss ?? 0) >= 0.5) return "Urgent";
  if ((finding.epss ?? 0) >= 0.1) return "High";
  return priorityForSeverity(finding.severity);
}

export function componentForPath(path = "") {
  const normalized = path.toLowerCase();
  if (normalized.includes("src/services/commerce/")) return "Commerce";
  if (normalized.includes("src/services/operations/")) return "Operations";
  if (normalized.includes("src/services/content/")) return "Content";
  if (normalized.includes("src/services/gateway/")) return "Gateway";
  if (normalized.includes("src/apps/") || normalized.includes("src/packages/")) return "Web";
  if (normalized.includes("src/shared/")) return "Platform";
  return "Infrastructure";
}

export function fingerprint(owner, repo, source, number) {
  return `${source}:${owner}/${repo}:${number}`;
}

export function fingerprintFromBody(body = "") {
  return body.match(markerPattern)?.[1] ?? null;
}

export function normalizeCodeScanning(alert, repository) {
  const path = alert.most_recent_instance?.location?.path ?? "";
  return {
    source: "code-scanning",
    number: alert.number,
    severity: severity(alert.rule?.security_severity_level ?? alert.rule?.severity),
    component: componentForPath(path),
    title: `[Code scanning] ${alert.rule?.description ?? alert.rule?.name ?? `Alert ${alert.number}`}`,
    url: alert.html_url,
    details: [
      `Tool: ${alert.tool?.name ?? "Code scanning"}`,
      `Rule: ${alert.rule?.id ?? alert.rule?.name ?? "unknown"}`,
      path ? `Location: \`${path}\`` : null,
      `Repository: ${repository}`,
    ].filter(Boolean),
  };
}

export function normalizeDependabot(alert, repository) {
  const dependency = alert.dependency ?? {};
  const advisory = alert.security_advisory ?? {};
  const packageName = dependency.package?.name ?? "dependency";
  const manifest = dependency.manifest_path ?? "";
  return {
    source: "dependabot",
    number: alert.number,
    severity: severity(advisory.severity),
    component: componentForPath(manifest),
    title: `[Dependabot] ${advisory.summary ?? `Vulnerable ${packageName}`}`,
    url: alert.html_url,
    cves: (advisory.identifiers ?? []).filter((identifier) => identifier.type === "CVE").map((identifier) => identifier.value),
    details: [
      `Package: \`${packageName}\` (${dependency.package?.ecosystem ?? "unknown ecosystem"})`,
      manifest ? `Manifest: \`${manifest}\`` : null,
      `Advisory: ${advisory.ghsa_id ?? "unknown"}`,
      `Repository: ${repository}`,
    ].filter(Boolean),
  };
}

export function applyDependabotRisk(finding, epssByCve, kevCves) {
  const scores = (finding.cves ?? []).map((cve) => epssByCve.get(cve) ?? 0);
  finding.epss = scores.length ? Math.max(...scores) : 0;
  finding.kev = (finding.cves ?? []).some((cve) => kevCves.has(cve));
  finding.details.push(`EPSS: ${(finding.epss * 100).toFixed(2)}%`);
  finding.details.push(`CISA KEV: ${finding.kev ? "Yes" : "No"}`);
  return finding;
}

export function normalizeSecretScanning(alert, repository) {
  return {
    source: "secret-scanning",
    number: alert.number,
    severity: alert.validity === "active" ? "critical" : "high",
    component: "Infrastructure",
    title: `[Secret scanning] Credential exposure requires human response`,
    url: alert.html_url,
    details: [
      `Secret type: ${alert.secret_type_display_name ?? alert.secret_type ?? "restricted"}`,
      `Validity: ${alert.validity ?? "unknown"}`,
      `Repository: ${repository}`,
      "Sensitive value and location details are intentionally omitted. Review the restricted alert directly.",
    ],
  };
}

export function issueBody(finding, owner, repo) {
  const key = fingerprint(owner, repo, finding.source, finding.number);
  return [
    `<!-- teck-security-fingerprint: ${key} -->`,
    "## Security finding",
    "",
    `- Recommended priority: ${priorityForFinding(finding)}`,
    ...finding.details.map((line) => `- ${line}`),
    "",
    `Alert: ${finding.url}`,
    "",
    "This issue is maintained by the security alert intake workflow. Close it only after GitHub verifies the underlying alert as resolved or dismissed.",
  ].join("\n");
}

function linkNext(value) {
  const next = value?.split(",").find((part) => part.includes('rel="next"'));
  return next?.match(/<([^>]+)>/)?.[1] ?? null;
}

export class GitHubClient {
  constructor(token) {
    this.token = token;
  }

  async request(url, options = {}, allowed = []) {
    const target = url.startsWith("http") ? url : `https://api.github.com${url}`;
    const response = await fetch(target, {
      ...options,
      headers: {
        Accept: "application/vnd.github+json",
        Authorization: `Bearer ${this.token}`,
        "X-GitHub-Api-Version": "2022-11-28",
        "User-Agent": "teck-security-alert-intake",
        ...options.headers,
      },
    });
    if (allowed.includes(response.status)) return { skipped: true, status: response.status };
    if (!response.ok) throw new Error(`GitHub API ${options.method ?? "GET"} ${url} failed (${response.status}): ${await response.text()}`);
    return { data: response.status === 204 ? null : await response.json(), headers: response.headers };
  }

  async paginate(path, allowed = []) {
    const items = [];
    let url = path;
    do {
      const response = await this.request(url, {}, allowed);
      if (response.skipped) return response;
      items.push(...response.data);
      url = linkNext(response.headers.get("link"));
    } while (url);
    return { data: items };
  }

  async graphql(query, variables) {
    const response = await this.request("/graphql", { method: "POST", body: JSON.stringify({ query, variables }) });
    if (response.data.errors) throw new Error(`GitHub GraphQL failed: ${response.data.errors.map((error) => error.message).join("; ")}`);
    return response.data.data;
  }
}

async function syncLabels(client, owner, repo, labels) {
  for (const label of labels) {
    const path = `/repos/${owner}/${repo}/labels/${encodeURIComponent(label.name)}`;
    const existing = await client.request(path, {}, [404]);
    if (existing.skipped) {
      await client.request(`/repos/${owner}/${repo}/labels`, { method: "POST", body: JSON.stringify(label) });
    } else {
      await client.request(path, { method: "PATCH", body: JSON.stringify({ new_name: label.name, color: label.color, description: label.description }) });
    }
  }
}

export function indexExistingIssues(issues) {
  const groups = new Map();
  for (const issue of issues.filter((candidate) => !candidate.pull_request)) {
    const key = fingerprintFromBody(issue.body);
    if (!key) continue;
    const group = groups.get(key) ?? [];
    group.push(issue);
    groups.set(key, group);
  }
  const existing = new Map();
  const duplicates = [];
  for (const [key, group] of groups) {
    group.sort((left, right) => left.number - right.number);
    existing.set(key, group[0]);
    duplicates.push(...group.slice(1));
  }
  return { existing, duplicates };
}

async function loadExistingIssues(client, owner, repo) {
  const response = await client.paginate(`/repos/${owner}/${repo}/issues?state=all&labels=${encodeURIComponent("security:tracked")}&per_page=100`);
  return indexExistingIssues(response.data);
}

async function reconcileDuplicateIssues(client, owner, repo, duplicates) {
  for (const issue of duplicates) {
    if (issue.state === "closed") continue;
    await client.request(`/repos/${owner}/${repo}/issues/${issue.number}`, {
      method: "PATCH",
      body: JSON.stringify({ state: "closed", state_reason: "not_planned" }),
    });
  }
}

async function collectFindings(client, owner, repo, config) {
  const repository = `${owner}/${repo}`;
  const definitions = [
    ["code-scanning", `/repos/${repository}/code-scanning/alerts?state=open&per_page=100`, normalizeCodeScanning],
    ["dependabot", `/repos/${repository}/dependabot/alerts?state=open&per_page=100`, normalizeDependabot],
    ["secret-scanning", `/repos/${repository}/secret-scanning/alerts?state=open&per_page=100`, normalizeSecretScanning],
  ];
  const findings = [];
  const available = new Set();
  for (const [source, path, normalize] of definitions) {
    if (!config.sources[source]?.enabled) continue;
    const response = await client.paginate(path, [403, 404]);
    if (response.skipped) {
      console.warn(`Skipping ${source}: API returned ${response.status}. Check the GitHub App permission and product availability.`);
      continue;
    }
    available.add(source);
    findings.push(...response.data.map((alert) => normalize(alert, repository)));
  }
  return { findings, available };
}

async function enrichDependabot(findings) {
  const dependencyFindings = findings.filter((finding) => finding.source === "dependabot");
  const cves = [...new Set(dependencyFindings.flatMap((finding) => finding.cves ?? []))];
  if (cves.length === 0) return;
  try {
    const [kevResponse, epssResponses] = await Promise.all([
      fetch("https://www.cisa.gov/sites/default/files/feeds/known_exploited_vulnerabilities.json"),
      Promise.all(Array.from({ length: Math.ceil(cves.length / 100) }, (_, index) => {
        const batch = cves.slice(index * 100, (index + 1) * 100).join(",");
        return fetch(`https://api.first.org/data/v1/epss?cve=${encodeURIComponent(batch)}`);
      })),
    ]);
    if (!kevResponse.ok || epssResponses.some((response) => !response.ok)) throw new Error("risk feed returned a non-success response");
    const kev = await kevResponse.json();
    const epss = await Promise.all(epssResponses.map((response) => response.json()));
    const kevCves = new Set((kev.vulnerabilities ?? []).map((entry) => entry.cveID));
    const epssByCve = new Map(epss.flatMap((response) => response.data ?? []).map((entry) => [entry.cve, Number(entry.epss)]));
    dependencyFindings.forEach((finding) => applyDependabotRisk(finding, epssByCve, kevCves));
  } catch (error) {
    console.warn(`Dependabot risk enrichment unavailable: ${error.message}`);
  }
}

async function upsertIssue(client, owner, repo, finding, existing, config) {
  const state = config.sources[finding.source].initialState;
  const labels = ["security", "security:tracked", `source:${finding.source}`, `severity:${finding.severity}`, state, finding.kev ? "known-exploited" : null].filter(Boolean);
  const body = issueBody(finding, owner, repo);
  if (!existing) {
    const response = await client.request(`/repos/${owner}/${repo}/issues`, { method: "POST", body: JSON.stringify({ title: finding.title, body, labels }) });
    return response.data;
  }
  const lifecycle = existing.state === "closed"
    ? state
    : existing.labels.map((label) => label.name).find((name) => name.startsWith("agent:"));
  const preserved = existing.labels.map((label) => label.name).filter((name) => !name.startsWith("severity:") && !name.startsWith("source:") && !name.startsWith("agent:") && name !== "known-exploited");
  const response = await client.request(`/repos/${owner}/${repo}/issues/${existing.number}`, {
    method: "PATCH",
    body: JSON.stringify({ title: finding.title, body, state: "open", labels: [...new Set([...preserved, `source:${finding.source}`, `severity:${finding.severity}`, lifecycle ?? state, finding.kev ? "known-exploited" : null].filter(Boolean))] }),
  });
  return response.data;
}

async function projectContext(client, config) {
  const query = `query($org:String!,$number:Int!){organization(login:$org){projectV2(number:$number){id fields(first:50){nodes{... on ProjectV2FieldCommon{id name dataType} ... on ProjectV2SingleSelectField{options{id name}}}}}}}`;
  const data = await client.graphql(query, { org: config.project.organization, number: config.project.number });
  const project = data.organization?.projectV2;
  if (!project) throw new Error(`Project ${config.project.organization}#${config.project.number} was not found`);
  return project;
}

async function addToProject(client, project, issue, finding, config) {
  const add = await client.graphql(`mutation($project:ID!,$content:ID!){addProjectV2ItemById(input:{projectId:$project,contentId:$content}){item{id}}}`, { project: project.id, content: issue.node_id });
  const itemId = add.addProjectV2ItemById.item.id;
  const desired = {
    [config.project.fields.status]: config.sources[finding.source].initialState === "agent:needs-input" ? "Blocked" : "Ready",
    [config.project.fields.workType]: "Security",
    [config.project.fields.component]: finding.component,
    [config.project.fields.kev]: finding.kev ? "Yes" : "No",
    [config.project.fields.epss]: finding.epss,
  };
  for (const field of project.fields.nodes) {
    const value = desired[field.name];
    if (value === undefined) continue;
    if (field.dataType === "NUMBER") {
      await client.graphql(`mutation($project:ID!,$item:ID!,$field:ID!,$number:Float!){updateProjectV2ItemFieldValue(input:{projectId:$project,itemId:$item,fieldId:$field,value:{number:$number}}){projectV2Item{id}}}`, { project: project.id, item: itemId, field: field.id, number: value });
      continue;
    }
    const option = field.options?.find((candidate) => candidate.name.toLowerCase() === String(value).toLowerCase());
    if (option) await client.graphql(`mutation($project:ID!,$item:ID!,$field:ID!,$option:String!){updateProjectV2ItemFieldValue(input:{projectId:$project,itemId:$item,fieldId:$field,value:{singleSelectOptionId:$option}}){projectV2Item{id}}}`, { project: project.id, item: itemId, field: field.id, option: option.id });
  }
}

async function reconcileResolved(client, owner, repo, existing, active, available) {
  for (const [key, issue] of existing) {
    const source = key.split(":", 1)[0];
    if (!available.has(source) || active.has(key) || issue.state === "closed") continue;
    await client.request(`/repos/${owner}/${repo}/issues/${issue.number}/comments`, { method: "POST", body: JSON.stringify({ body: "The underlying GitHub security alert is no longer open. Closing this tracking issue after automated reconciliation." }) });
    await client.request(`/repos/${owner}/${repo}/issues/${issue.number}`, { method: "PATCH", body: JSON.stringify({ state: "closed", state_reason: "completed" }) });
  }
}

export async function run() {
  const token = process.env.GITHUB_TOKEN;
  if (!token) throw new Error("GITHUB_TOKEN is required");
  const [owner, repo] = (process.env.GITHUB_REPOSITORY ?? "").split("/");
  if (!owner || !repo) throw new Error("GITHUB_REPOSITORY must be owner/repo");
  const config = JSON.parse(await readFile(configUrl, "utf8"));
  const client = new GitHubClient(token);
  const { findings, available } = await collectFindings(client, owner, repo, config);
  await enrichDependabot(findings);
  const project = await projectContext(client, config);
  if (process.env.SECURITY_INTAKE_DRY_RUN === "true") {
    const summary = findings.reduce((counts, finding) => {
      counts[finding.source] = (counts[finding.source] ?? 0) + 1;
      return counts;
    }, {});
    console.log(JSON.stringify({
      dryRun: true,
      project: `${config.project.organization}#${config.project.number}`,
      availableSources: [...available].sort(),
      findings: summary,
      dependabotRisk: {
        knownExploited: findings.filter((finding) => finding.source === "dependabot" && finding.kev).length,
        epssAtLeast10Percent: findings.filter((finding) => finding.source === "dependabot" && (finding.epss ?? 0) >= 0.1).length,
        epssAtLeast50Percent: findings.filter((finding) => finding.source === "dependabot" && (finding.epss ?? 0) >= 0.5).length,
      },
    }, null, 2));
    return;
  }
  await syncLabels(client, owner, repo, config.labels);
  const { existing, duplicates } = await loadExistingIssues(client, owner, repo);
  await reconcileDuplicateIssues(client, owner, repo, duplicates);
  const active = new Set();
  for (const finding of findings) {
    const key = fingerprint(owner, repo, finding.source, finding.number);
    active.add(key);
    const issue = await upsertIssue(client, owner, repo, finding, existing.get(key), config);
    await addToProject(client, project, issue, finding, config);
  }
  await reconcileResolved(client, owner, repo, existing, active, available);
  console.log(`Synchronized ${findings.length} open security alert(s) into ${config.project.organization} Project #${config.project.number}.`);
}

const invokedPath = process.argv[1] ? pathToFileURL(process.argv[1]).href : null;
if (invokedPath === import.meta.url) run().catch((error) => { console.error(error); process.exitCode = 1; });

import { createHmac } from "node:crypto";
import { readFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";

const configUrl = new URL("../security-intake.json", import.meta.url);
const payloadSchema = "teck/security-alert-snapshot/v1";

export function severity(value) {
  const normalized = String(value ?? "medium").toLowerCase();
  if (normalized === "error") return "high";
  if (normalized === "warning" || normalized === "moderate") return "medium";
  return ["low", "medium", "high", "critical"].includes(normalized)
    ? normalized
    : "medium";
}

export function priorityForSeverity(value) {
  return {
    low: "Low",
    medium: "Medium",
    high: "High",
    critical: "Urgent",
  }[severity(value)];
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
  if (
    normalized.includes("src/apps/") ||
    normalized.includes("src/packages/")
  ) {
    return "Web";
  }
  if (normalized.includes("src/shared/")) return "Platform";
  return "Infrastructure";
}

export function fingerprint(owner, repo, source, number) {
  return `${source}:${owner}/${repo}:${number}`;
}

export function normalizeCodeScanning(alert, repository) {
  const path = alert.most_recent_instance?.location?.path ?? "";
  return {
    source: "code-scanning",
    number: alert.number,
    severity: severity(
      alert.rule?.security_severity_level ?? alert.rule?.severity,
    ),
    component: componentForPath(path),
    title: `[Code scanning] ${
      alert.rule?.description ?? alert.rule?.name ?? `Alert ${alert.number}`
    }`,
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
    cves: (advisory.identifiers ?? [])
      .filter((identifier) => identifier.type === "CVE")
      .map((identifier) => identifier.value),
    details: [
      `Package: \`${packageName}\` (${
        dependency.package?.ecosystem ?? "unknown ecosystem"
      })`,
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
    title: "[Secret scanning] Credential exposure requires human response",
    url: alert.html_url,
    details: [
      `Secret type: ${
        alert.secret_type_display_name ?? alert.secret_type ?? "restricted"
      }`,
      `Validity: ${alert.validity ?? "unknown"}`,
      `Repository: ${repository}`,
      "Sensitive value and location details are intentionally omitted. Review the restricted alert directly.",
    ],
  };
}

export function buildPaperclipSnapshot(
  owner,
  repo,
  findings,
  availableSources,
  generatedAt = new Date().toISOString(),
) {
  const repository = `${owner}/${repo}`;
  return {
    schema: payloadSchema,
    repository,
    generatedAt,
    authoritativeSnapshot: true,
    availableSources: [...availableSources].sort(),
    findings: findings
      .map((finding) => ({
        id: fingerprint(owner, repo, finding.source, finding.number),
        source: finding.source,
        number: finding.number,
        severity: severity(finding.severity),
        recommendedPriority: priorityForFinding(finding),
        component: finding.component,
        title: finding.title,
        url: finding.url,
        details: [...finding.details],
        cves: finding.source === "dependabot" ? [...(finding.cves ?? [])] : [],
        epss:
          finding.source === "dependabot" && Number.isFinite(finding.epss)
            ? finding.epss
            : null,
        kev: finding.source === "dependabot" ? finding.kev === true : false,
        requiresHumanInput: finding.source === "secret-scanning",
      }))
      .sort((left, right) => left.id.localeCompare(right.id)),
  };
}

export function githubSignature(body, secret) {
  return `sha256=${createHmac("sha256", secret).update(body).digest("hex")}`;
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
    const target = url.startsWith("http")
      ? url
      : `https://api.github.com${url}`;
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
    if (allowed.includes(response.status)) {
      return { skipped: true, status: response.status };
    }
    if (!response.ok) {
      throw new Error(
        `GitHub API ${options.method ?? "GET"} ${url} failed (${
          response.status
        }): ${await response.text()}`,
      );
    }
    return {
      data: response.status === 204 ? null : await response.json(),
      headers: response.headers,
    };
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
}

async function collectFindings(client, owner, repo, config) {
  const repository = `${owner}/${repo}`;
  const definitions = [
    [
      "code-scanning",
      `/repos/${repository}/code-scanning/alerts?state=open&per_page=100`,
      normalizeCodeScanning,
    ],
    [
      "dependabot",
      `/repos/${repository}/dependabot/alerts?state=open&per_page=100`,
      normalizeDependabot,
    ],
    [
      "secret-scanning",
      `/repos/${repository}/secret-scanning/alerts?state=open&per_page=100`,
      normalizeSecretScanning,
    ],
  ];
  const findings = [];
  const available = new Set();
  for (const [source, path, normalize] of definitions) {
    if (!config.sources[source]?.enabled) continue;
    const response = await client.paginate(path, [403, 404]);
    if (response.skipped) {
      console.warn(
        `Skipping ${source}: API returned ${response.status}. Check the GitHub App permission and product availability.`,
      );
      continue;
    }
    available.add(source);
    findings.push(
      ...response.data.map((alert) => normalize(alert, repository)),
    );
  }
  return { findings, available };
}

async function enrichDependabot(findings) {
  const dependencyFindings = findings.filter(
    (finding) => finding.source === "dependabot",
  );
  const cves = [
    ...new Set(dependencyFindings.flatMap((finding) => finding.cves ?? [])),
  ];
  if (cves.length === 0) return;
  try {
    const [kevResponse, epssResponses] = await Promise.all([
      fetch(
        "https://www.cisa.gov/sites/default/files/feeds/known_exploited_vulnerabilities.json",
      ),
      Promise.all(
        Array.from({ length: Math.ceil(cves.length / 100) }, (_, index) => {
          const batch = cves.slice(index * 100, (index + 1) * 100).join(",");
          return fetch(
            `https://api.first.org/data/v1/epss?cve=${encodeURIComponent(batch)}`,
          );
        }),
      ),
    ]);
    if (!kevResponse.ok || epssResponses.some((response) => !response.ok)) {
      throw new Error("risk feed returned a non-success response");
    }
    const kev = await kevResponse.json();
    const epss = await Promise.all(
      epssResponses.map((response) => response.json()),
    );
    const kevCves = new Set(
      (kev.vulnerabilities ?? []).map((entry) => entry.cveID),
    );
    const epssByCve = new Map(
      epss
        .flatMap((response) => response.data ?? [])
        .map((entry) => [entry.cve, Number(entry.epss)]),
    );
    for (const finding of dependencyFindings) {
      applyDependabotRisk(finding, epssByCve, kevCves);
    }
  } catch (error) {
    console.warn(`Dependabot risk enrichment unavailable: ${error.message}`);
  }
}

async function postSnapshot(url, secret, snapshot) {
  const target = new URL(url);
  if (target.protocol !== "https:") {
    throw new Error("PAPERCLIP_SECURITY_WEBHOOK_URL must use HTTPS");
  }
  const body = JSON.stringify(snapshot);
  const response = await fetch(target, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "User-Agent": "teck-security-alert-intake",
      "X-GitHub-Event": "teck_security_alert_snapshot",
      "X-Hub-Signature-256": githubSignature(body, secret),
    },
    body,
  });
  if (!response.ok) {
    throw new Error(
      `Paperclip security webhook failed (${response.status}): ${await response.text()}`,
    );
  }
}

export async function run() {
  const token = process.env.GITHUB_TOKEN;
  if (!token) throw new Error("GITHUB_TOKEN is required");
  const [owner, repo] = (process.env.GITHUB_REPOSITORY ?? "").split("/");
  if (!owner || !repo) {
    throw new Error("GITHUB_REPOSITORY must be owner/repo");
  }
  const config = JSON.parse(await readFile(configUrl, "utf8"));
  const client = new GitHubClient(token);
  const { findings, available } = await collectFindings(
    client,
    owner,
    repo,
    config,
  );
  await enrichDependabot(findings);
  const snapshot = buildPaperclipSnapshot(owner, repo, findings, available);

  if (process.env.SECURITY_INTAKE_DRY_RUN === "true") {
    const summary = findings.reduce((counts, finding) => {
      counts[finding.source] = (counts[finding.source] ?? 0) + 1;
      return counts;
    }, {});
    console.log(
      JSON.stringify(
        {
          dryRun: true,
          schema: snapshot.schema,
          repository: snapshot.repository,
          availableSources: snapshot.availableSources,
          findings: summary,
        },
        null,
        2,
      ),
    );
    return;
  }

  const webhookUrl = process.env.PAPERCLIP_SECURITY_WEBHOOK_URL;
  const webhookSecret = process.env.PAPERCLIP_SECURITY_WEBHOOK_SECRET;
  if (!webhookUrl || !webhookSecret) {
    throw new Error(
      "PAPERCLIP_SECURITY_WEBHOOK_URL and PAPERCLIP_SECURITY_WEBHOOK_SECRET are required",
    );
  }
  await postSnapshot(webhookUrl, webhookSecret, snapshot);
  console.log(
    `Forwarded ${snapshot.findings.length} sanitized open security alert(s) to Paperclip.`,
  );
}

const invokedPath = process.argv[1]
  ? pathToFileURL(process.argv[1]).href
  : null;
if (invokedPath === import.meta.url) {
  run().catch((error) => {
    console.error(error);
    process.exitCode = 1;
  });
}

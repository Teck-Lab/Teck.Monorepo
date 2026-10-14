import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import autoCore from "@auto-it/core";
import { inc, prerelease, rcompare, valid } from "semver";

const { Auto, determineNextVersion } = autoCore;

export const releaseBranches = ["main", "alpha", "beta", "next"];
export function git(...args) {
  return execFileSync("git", args, { encoding: "utf8" }).trim();
}
export function channelFor(branch) {
  if (!releaseBranches.includes(branch)) throw new Error(`Unsupported release branch: ${branch}`);
  return branch === "main" ? "stable" : branch;
}
export function selectPlatformTag(tags, channel = "stable") {
  return (
    tags
      .filter((tag) => /^v\d+\.\d+\.\d+/.test(tag) && valid(tag))
      .filter((tag) => (channel === "stable" ? !prerelease(tag) : prerelease(tag)?.[0] === channel))
      .sort(rcompare)[0] ?? null
  );
}
export function platformVersion({ channel, stableTag, candidateTag, bump }) {
  if (!bump) return null;
  const stable = stableTag ?? "v0.0.0";
  const version =
    channel === "stable"
      ? inc(stable, bump)
      : determineNextVersion(stable, candidateTag ?? stable, bump, channel);
  if (!valid(version)) throw new Error(`Auto could not calculate a platform version: ${version}`);
  return valid(version);
}
export function releaseChecksPassed(runs, sha, branch) {
  return ["CI", "Security Scans"].every((name) => {
    const matching = runs.filter(
      (run) =>
        run.name === name &&
        run.head_sha === sha &&
        run.head_branch === branch &&
        run.event === "push",
    );
    matching.sort((a, b) => b.id - a.id || b.run_attempt - a.run_attempt);
    return matching[0]?.status === "completed" && matching[0]?.conclusion === "success";
  });
}
export async function createPlatformAuto({ branch, sourceSha }) {
  channelFor(branch);
  if (!process.env.GH_TOKEN)
    throw new Error("GH_TOKEN is required for Auto's GitHub release integration.");
  const auto = new Auto({ baseBranch: "main", quiet: true, disableTsNode: true });
  await auto.loadConfig();
  // workflow_run exposes the default branch in the CI environment. Supply the
  // validated source branch explicitly instead of inheriting that context.
  auto.inPrereleaseBranch = () => branch !== "main";
  auto.hooks.onCreateChangelog.tap("Teck source branch", (changelog) => {
    changelog.hooks.renderChangelogTitle.tapPromise("Teck source branch", async (label) =>
      label === "pushToBaseBranch" ? `#### ⚠️ Pushed to \`${branch}\`\n` : undefined,
    );
  });
  const tags = git("tag", "--merged", sourceSha, "--list", "v*").split(/\r?\n/);
  const stableTag = selectPlatformTag(git("tag", "--list", "v*").split(/\r?\n/));
  if (stableTag) {
    try {
      git("merge-base", "--is-ancestor", stableTag, sourceSha);
    } catch {
      throw new Error(
        `Merge the latest stable release ${stableTag} into ${branch} before publishing.`,
      );
    }
  }
  const candidateTag = branch === "main" ? null : selectPlatformTag(tags, branch);
  const firstCommit = git("rev-list", "--max-parents=0", sourceSha).split(/\r?\n/)[0];
  // Keep Auto's platform history separate from Nx component tags.
  auto.git.getLatestRelease = async () => stableTag ?? "v0.0.0";
  auto.git.getLatestTagInBranch = async () => candidateTag ?? stableTag ?? "v0.0.0";
  auto.git.getLastTagNotInBaseBranch = async () => candidateTag;
  return { auto, stableTag, candidateTag, firstCommit };
}
export function platformNotes(manifest, groupNotes, autoNotes) {
  const lines = [
    `# Teck platform ${manifest.platform.version}`,
    "",
    "## Components",
    "",
    "| Component | Version |",
    "| --- | --- |",
    ...manifest.components.map((unit) => `| ${unit.name} | ${unit.version} |`),
    "",
  ];
  for (const unit of manifest.components.filter((component) => component.changed)) {
    lines.push(
      `## ${unit.name}`,
      "",
      groupNotes[unit.name] || "Dependency-driven version update.",
      "",
    );
  }
  lines.push("## Platform changes", "", autoNotes || "No additional release notes.", "");
  return lines.join("\n");
}
export function readManifest(path = ".nx/releases/manifest.json") {
  return JSON.parse(readFileSync(path, "utf8"));
}

export function githubReleaseBody(manifest, notes) {
  if (notes.length <= 120_000) return notes;
  return [
    `# Teck platform ${manifest.platform.version}`,
    "",
    "The complete notes, grouped by component, are attached as `release-notes.md`.",
    "",
    "| Component | Version |",
    "| --- | --- |",
    ...manifest.components.map((unit) => `| ${unit.name} | ${unit.version} |`),
  ].join("\n");
}

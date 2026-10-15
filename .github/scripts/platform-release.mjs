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
export function releaseSourceAlreadyPublished(
  { sourceSha, stableTag, candidateTag },
  resolveTag = (tag) => git("rev-list", "-n", "1", tag),
) {
  return [stableTag, candidateTag].some((tag) => tag && resolveTag(tag) === sourceSha);
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
export function releaseOverview(manifest, groupNotes = {}) {
  const changed = manifest.components.filter((unit) => unit.changed);
  const highlights = [
    ...new Set(
      changed.flatMap((unit) =>
        (groupNotes[unit.name] ?? "")
          .split("\n")
          .filter((line) => line.startsWith("- "))
          .slice(0, 2)
          .map((line) =>
            line
              .slice(2)
              .replace(/\s+\(\[.*$/, "")
              .replaceAll("**", ""),
          ),
      ),
    ),
  ].slice(0, 4);
  const channel =
    manifest.platform.channel === "stable" ? "release" : `${manifest.platform.channel} prerelease`;
  return `This ${channel} updates ${changed.length} of ${manifest.components.length} components${changed.length ? `: ${changed.map((unit) => unit.name).join(", ")}` : ""}.${highlights.length ? ` Highlights: ${highlights.join("; ")}.` : ""}`;
}
function componentHeading(unit) {
  return `## 📦 ${unit.name} · v${unit.version}`;
}
export function platformNotes(manifest, groupNotes, autoNotes) {
  const changed = manifest.components.filter((component) => component.changed);
  const lines = [
    `# 🚀 Teck platform ${manifest.platform.version}`,
    "",
    manifest.releaseSummary || releaseOverview(manifest, groupNotes),
    "",
  ];
  if (changed.length) lines.push("## 🧩 Components", "");
  for (const unit of changed) {
    lines.push(
      componentHeading(unit),
      "",
      groupNotes[unit.name]?.trim() || "🔗 Dependency-driven version update.",
      "",
    );
  }
  lines.push("## 🛠️ Platform changes", "", autoNotes || "No additional release notes.", "");
  const repository = process.env.GITHUB_REPOSITORY ?? "Teck-Lab/Teck.Monorepo";
  const baseline = manifest.platform.from;
  if (baseline) {
    lines.push(
      "---",
      "",
      `🔎 [Compare ${manifest.platform.stableTag || manifest.platform.candidateTag ? "changes since the previous release" : "changes from the initial commit"}](https://github.com/${repository}/compare/${encodeURIComponent(baseline)}...${encodeURIComponent(manifest.platform.tag ?? `v${manifest.platform.version}`)})`,
      "",
    );
  }
  return lines.join("\n");
}
export function readManifest(path = ".nx/releases/manifest.json") {
  return JSON.parse(readFileSync(path, "utf8"));
}

export function githubReleaseBody(manifest, notes) {
  const maxBytes = 120_000;
  // Partition only our outer sections; change categories stay inside their
  // component. Give each section room so one large history cannot hide
  // the remaining components or the platform changes.
  const headings = [
    ...manifest.components.filter((unit) => unit.changed).map(componentHeading),
    "## 🛠️ Platform changes",
  ];
  const starts = headings
    .map((heading) => notes.indexOf(`\n${heading}\n`))
    .filter((index) => index >= 0)
    .map((index) => index + 1)
    .sort((a, b) => a - b);
  // The comparison link remains outside the collapsed sections and outside
  // their truncation budget, so it always appears at the bottom.
  const footerStart = notes.lastIndexOf("\n---\n\n🔎 ");
  const footer = footerStart >= 0 ? `\n${notes.slice(footerStart)}` : "";
  const content = footerStart >= 0 ? notes.slice(0, footerStart) : notes;
  const sections = starts.length
    ? starts.map((start, index) => content.slice(start, starts[index + 1]))
    : [content];
  if (!starts.length && Buffer.byteLength(notes) <= maxBytes) return notes;
  const summaries = sections.map((section) =>
    section.startsWith("## ") ? section.split("\n", 1)[0].slice(3) : "Changes",
  );
  const contents = sections.map((section) =>
    (section.startsWith("## ") ? section.slice(section.indexOf("\n") + 1) : section).trim(),
  );
  const collapse = (section, index) =>
    `<details>\n<summary><h2>${summaries[index]}</h2></summary>\n\n${section.trimEnd()}\n\n</details>`;
  const completeBody =
    content.slice(0, starts[0] ?? 0) + contents.map(collapse).join("\n\n") + footer;
  if (Buffer.byteLength(completeBody) <= maxBytes) return completeBody;
  const prefix = `${notes.slice(0, starts[0] ?? 0)}Some sections are shortened below. The complete changelog is attached as \`release-notes.md\`.\n\n`;
  const wrapperBytes = sections.reduce(
    (bytes, _section, index) => bytes + Buffer.byteLength(collapse("", index)),
    0,
  );
  const sectionBudget = Math.floor(
    (maxBytes -
      Buffer.byteLength(prefix) -
      Buffer.byteLength(footer) -
      wrapperBytes -
      (sections.length - 1) * 2) /
      sections.length,
  );
  const suffix = "\n\n_Additional changes are listed in the attached release-notes.md._";
  return (
    prefix +
    contents
      .map((section) => {
        if (Buffer.byteLength(section) <= sectionBudget) return section.trimEnd();
        const lines = [];
        let remaining = sectionBudget - Buffer.byteLength(suffix);
        for (const line of section.trimEnd().split("\n")) {
          const bytes = Buffer.byteLength(line) + (lines.length ? 1 : 0);
          if (bytes > remaining) break;
          lines.push(line);
          remaining -= bytes;
        }
        // Keep complete Markdown lines, including PR links, rather than cutting
        // the changelog in the middle of a link or a multibyte character.
        return lines.join("\n").trimEnd() + suffix;
      })
      .map(collapse)
      .join("\n\n") +
    footer
  );
}

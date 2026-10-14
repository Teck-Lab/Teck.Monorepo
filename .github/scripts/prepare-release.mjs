#!/usr/bin/env node
import { appendFileSync, mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { ReleaseClient, releaseVersion } from "nx/release";
import { groupChangelogRenderer } from "./group-changelog.mjs";
import { channelFor, createPlatformAuto, git, platformVersion } from "./platform-release.mjs";
import { installReleaseScopeAliases } from "./release-scopes.mjs";

installReleaseScopeAliases();
const dryRun = process.argv.includes("--dry-run");
const branch = process.env.RELEASE_BRANCH ?? git("branch", "--show-current");
const channel = channelFor(branch);
const sourceSha = process.env.RELEASE_SOURCE_SHA ?? git("rev-parse", "HEAD");
if (sourceSha !== git("rev-parse", "HEAD"))
  throw new Error("Release source must match the checked-out commit.");
const { auto, stableTag, candidateTag, firstCommit } = await createPlatformAuto({
  branch,
  sourceSha,
});
const from = candidateTag ?? stableTag ?? firstCommit;
const bump = await auto.release.getSemverBump(stableTag ?? firstCommit, sourceSha);
const deltaBump = await auto.release.getSemverBump(from, sourceSha);
const versionResult = await releaseVersion({
  dryRun: true,
  firstRelease: git("tag", "--list", "*@v*") === "",
  verbose: false,
  preid: channel === "stable" ? undefined : channel,
  stageChanges: false,
  gitCommit: false,
  gitTag: false,
  gitPush: false,
});
const components = [];
for (const group of versionResult.releaseGraph.releaseGroups) {
  const projects = [
    ...(versionResult.releaseGraph.releaseGroupToFilteredProjects.get(group) ?? []),
  ];
  const units =
    group.projectsRelationship === "independent"
      ? projects.map((project) => [project])
      : [projects];
  for (const unit of units) {
    if (!unit.length) continue;
    const data = unit.map((project) => versionResult.projectsVersionData[project]);
    const changed = data.some((project) => project?.newVersion || project?.dockerVersion);
    const versions = [
      ...new Set(
        data
          .map(
            (project) => project?.dockerVersion || project?.newVersion || project?.currentVersion,
          )
          .filter(Boolean),
      ),
    ];
    if (versions.length !== 1)
      throw new Error(`Inconsistent Nx versions for ${group.name}: ${versions}`);
    const version = versions[0];
    const name = group.projectsRelationship === "independent" ? unit[0] : group.name;
    const tag = group.releaseTag.pattern
      .replaceAll("{version}", version)
      .replaceAll("{projectName}", unit[0])
      .replaceAll("{releaseGroupName}", group.name);
    const currentTag = git("tag", "--merged", sourceSha, "--list", tag);
    components.push({
      name,
      releaseGroup: group.name,
      projects: unit.sort(),
      version,
      tag,
      changed: changed || !currentTag,
      sourceSha: changed || !currentTag ? sourceSha : git("rev-list", "-n", "1", tag),
      images: [],
    });
  }
}
components.sort((a, b) => a.name.localeCompare(b.name));
const hasReleases = Boolean(bump && (deltaBump || components.some((unit) => unit.changed)));
if (!dryRun && process.env.GITHUB_OUTPUT)
  appendFileSync(process.env.GITHUB_OUTPUT, `has-releases=${hasReleases}\n`);
if (!hasReleases) {
  console.log("No new releasable platform changes.");
  process.exit(0);
}
const version = platformVersion({ channel, stableTag, candidateTag, bump });
const changelogClient = new ReleaseClient({
  groups: Object.fromEntries(
    versionResult.releaseGraph.releaseGroups
      .filter((group) => group.projectsRelationship === "fixed")
      .map((group) => [
        group.name,
        {
          changelog: {
            renderer: groupChangelogRenderer(group.projects),
          },
        },
      ]),
  ),
});
const changelog = await changelogClient.releaseChangelog({
  dryRun: true,
  firstRelease: !stableTag,
  from,
  versionData: versionResult.projectsVersionData,
  forceChangelogGeneration: true,
  createRelease: false,
  stageChanges: false,
  gitCommit: false,
  gitTag: false,
  gitPush: false,
});
const groupNotes = Object.fromEntries(
  components
    .filter((unit) => unit.changed)
    .map((unit) => [
      unit.name,
      [
        ...new Set(
          unit.projects
            .map((project) => changelog.projectChangelogs?.[project]?.contents)
            .filter(Boolean),
        ),
      ].join("\n\n"),
    ]),
);
const manifest = {
  schemaVersion: 2,
  sourceSha,
  branch,
  platform: { version, tag: `v${version}`, channel, from, stableTag, candidateTag },
  components,
  groupNotes,
  autoNotes: await auto.release.generateReleaseNotes(from, sourceSha),
};
if (dryRun) {
  console.log(JSON.stringify(manifest, null, 2));
} else {
  mkdirSync(".nx/releases", { recursive: true });
  writeFileSync(join(".nx/releases", "manifest.json"), `${JSON.stringify(manifest, null, 2)}\n`);
  console.log(
    `Prepared platform ${version}: ${components.filter((unit) => unit.changed).length} changed components.`,
  );
}

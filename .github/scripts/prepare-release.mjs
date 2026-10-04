#!/usr/bin/env node

import { execFileSync } from "node:child_process";
import { existsSync, mkdirSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import process from "node:process";
import { releaseChangelog, releaseVersion } from "nx/release";

const dryRun = process.argv.includes("--dry-run");
const manifestPath = process.env.NX_RELEASE_MANIFEST ?? ".nx/releases/manifest.json";
const notesDirectory = join(dirname(manifestPath), "notes");

function git(...args) {
  return execFileSync("git", args, { encoding: "utf8" }).trim();
}

function releaseTag(pattern, version, projectName, releaseGroupName) {
  return pattern
    .replaceAll("{version}", version)
    .replaceAll("{projectName}", projectName ?? "")
    .replaceAll("{releaseGroupName}", releaseGroupName ?? "");
}

function slug(value) {
  return value
    .replace(/^@/, "")
    .replaceAll("/", "-")
    .replace(/[^A-Za-z0-9._-]/g, "-");
}

function latestTag(pattern, projectName, releaseGroupName) {
  const match = releaseTag(pattern, "*", projectName, releaseGroupName);
  const tags = git("tag", "--list", match, "--sort=-version:refname")
    .split(/\r?\n/)
    .filter(Boolean);
  return tags[0] ?? null;
}

function unique(values) {
  return [...new Set(values)];
}

const existingReleaseTags = git("tag", "--list", "*@v*").split(/\r?\n/).filter(Boolean);
const firstRelease = existingReleaseTags.length === 0;
const sourceSha =
  process.env.RELEASE_SOURCE_SHA ?? process.env.GITHUB_SHA ?? git("rev-parse", "HEAD");

const versionResult = await releaseVersion({
  dryRun,
  firstRelease,
  verbose: true,
  stageChanges: false,
  gitCommit: false,
  gitTag: false,
  gitPush: false,
  deleteVersionPlans: false,
});

const changelogResult = await releaseChangelog({
  dryRun,
  firstRelease,
  verbose: true,
  versionData: versionResult.projectsVersionData,
  releaseGraph: versionResult.releaseGraph,
  forceChangelogGeneration: true,
  createRelease: false,
  stageChanges: false,
  gitCommit: false,
  gitTag: false,
  gitPush: false,
  deleteVersionPlans: !dryRun,
});

const releases = [];
for (const group of versionResult.releaseGraph.releaseGroups) {
  const projects = [
    ...(versionResult.releaseGraph.releaseGroupToFilteredProjects.get(group) ?? []),
  ];
  const changedProjects = projects.filter((project) => {
    const data = versionResult.projectsVersionData[project];
    return data && (data.dockerVersion || data.newVersion);
  });

  if (changedProjects.length === 0) {
    continue;
  }

  const units =
    group.projectsRelationship === "independent"
      ? changedProjects.map((project) => ({
          name: project,
          projects: [project],
          projectName: project,
        }))
      : [
          {
            name: group.name,
            projects: changedProjects,
            projectName: changedProjects[0],
          },
        ];

  for (const unit of units) {
    const versions = unique(
      unit.projects.map((project) => {
        const data = versionResult.projectsVersionData[project];
        return data.dockerVersion || data.newVersion;
      }),
    );
    if (versions.length !== 1 || !versions[0]) {
      throw new Error(
        `Release unit ${unit.name} resolved inconsistent versions: ${versions.join(", ")}`,
      );
    }

    const version = versions[0];
    const tag = releaseTag(group.releaseTag.pattern, version, unit.projectName, group.name);
    const configName = `release-drafter/${slug(unit.name)}.yml`;
    const configPath = join(".github", configName);
    if (!existsSync(configPath)) {
      throw new Error(`Missing Release Drafter configuration for ${unit.name}: ${configPath}`);
    }

    const notes = unique(
      unit.projects
        .map((project) => changelogResult.projectChangelogs?.[project]?.contents)
        .filter(Boolean),
    ).join("\n\n");
    const notesFile = join(notesDirectory, `${slug(unit.name)}.md`);

    releases.push({
      name: unit.name,
      releaseGroup: group.name,
      sourceSha,
      projects: unit.projects.sort(),
      version,
      tag,
      previousTag: latestTag(group.releaseTag.pattern, unit.projectName, group.name),
      configName,
      notesFile: notesFile.replaceAll("\\", "/"),
    });

    if (!dryRun) {
      mkdirSync(notesDirectory, { recursive: true });
      writeFileSync(
        notesFile,
        notes || `No user-facing changes were generated for ${unit.name}.`,
        "utf8",
      );
    }
  }
}

releases.sort((left, right) => left.name.localeCompare(right.name));
if (releases.length === 0) {
  console.log("Nx found no release units in the current Version Plans.");
  process.exit(0);
}

const manifest = {
  schemaVersion: 1,
  sourceSha,
  releases,
};

if (dryRun) {
  console.log(JSON.stringify(manifest, null, 2));
} else {
  mkdirSync(dirname(manifestPath), { recursive: true });
  if (existsSync(notesDirectory)) {
    for (const file of readdirSync(notesDirectory)) {
      if (!releases.some((release) => release.notesFile.endsWith(`/${file}`))) {
        rmSync(join(notesDirectory, file));
      }
    }
  }
  writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, "utf8");
  console.log(`Wrote ${manifestPath} with ${releases.length} release unit(s).`);
}

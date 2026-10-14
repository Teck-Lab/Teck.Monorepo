import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { readdirSync, readFileSync, writeFileSync } from "node:fs";
import { pathToFileURL } from "node:url";
import {
  createPlatformAuto,
  git,
  githubReleaseBody,
  platformNotes,
  readManifest,
} from "./platform-release.mjs";

export async function completeManifest(manifest, services, results, resolveImage, promoteImage) {
  const completed = structuredClone(manifest);
  const promotions = [];
  for (const service of services) {
    const unit = completed.components.find(
      (component) => component.releaseGroup === service.releaseGroup,
    );
    assert.ok(unit, `Missing release component for ${service.product}`);
    const image = `ghcr.io/${(process.env.GITHUB_REPOSITORY_OWNER ?? "Teck-Lab").toLowerCase()}/teck-monorepo/${service.group}/${service.service}`;
    let digest;
    if (unit.changed) {
      const result = results.find((entry) => entry.image === image);
      assert.ok(result, `No signed build result for ${service.product}`);
      assert.equal(result.sourceSha, manifest.sourceSha, "Candidate source mismatch");
      assert.equal(result.artifactVersion, unit.version, "Candidate artifact version mismatch");
      assert.equal(
        result.version,
        `${unit.version}-build.${manifest.sourceSha}`,
        "Candidate tag mismatch",
      );
      digest = await resolveImage(`${image}:${result.version}`);
      assert.equal(digest, result.digest, "Candidate digest mismatch");
      promotions.push([image, digest, unit.version, manifest.platform.channel]);
    } else {
      digest = await resolveImage(`${image}:${unit.version}`);
    }
    assert.match(digest, /^sha256:[a-f0-9]{64}$/);
    unit.images.push({ service: service.product, image, digest, ref: `${image}@${digest}` });
  }
  // Validate the complete image set before changing any published tags.
  for (const promotion of promotions) await promoteImage(...promotion);
  delete completed.groupNotes;
  delete completed.autoNotes;
  return completed;
}

export async function ensureGitTag(github, repository, tag, sha, { createIfMissing = true } = {}) {
  const [owner, repo] = repository.split("/");
  try {
    const { data } = await github.git.getRef({ owner, repo, ref: `tags/${tag}` });
    let object = data.object;
    while (object.type === "tag")
      object = (await github.git.getTag({ owner, repo, tag_sha: object.sha })).data.object;
    assert.equal(object.sha, sha, `Refusing to overwrite ${tag}`);
  } catch (error) {
    if (error.status !== 404) throw error;
    if (createIfMissing) await github.git.createRef({ owner, repo, ref: `refs/tags/${tag}`, sha });
  }
}

export async function notificationPrs(auto, commits, isPrerelease) {
  const eligible = new Set();
  for (const number of new Set(
    commits.map((commit) => commit.pullRequest?.number).filter(Boolean),
  )) {
    const labels = await auto.git.getLabels(number);
    if (!labels.includes("released") && (!isPrerelease || !labels.includes("pre-released")))
      eligible.add(number);
  }
  return eligible;
}

export function installNotificationFilter(auto, eligible) {
  auto.hooks.afterRelease.intercept({
    call(context) {
      context.commits = context.commits.filter(
        (commit) => !commit.pullRequest || eligible.has(commit.pullRequest.number),
      );
    },
  });
}

function inspectImage(ref) {
  const output = execFileSync("docker", ["buildx", "imagetools", "inspect", ref], {
    encoding: "utf8",
  });
  const digest = output.match(/^Digest:\s+(sha256:[a-f0-9]{64})/m)?.[1];
  assert.ok(digest, `No image digest for ${ref}`);
  return digest;
}
function promoteImage(image, digest, version, channel) {
  try {
    assert.equal(
      inspectImage(`${image}:${version}`),
      digest,
      `Refusing to replace immutable image ${image}:${version}`,
    );
  } catch (error) {
    if (!/not found|manifest unknown|404/.test(String(error.stderr))) throw error;
  }
  execFileSync(
    "docker",
    [
      "buildx",
      "imagetools",
      "create",
      "--prefer-index=false",
      "--tag",
      `${image}:${version}`,
      "--tag",
      `${image}:${channel}`,
      `${image}@${digest}`,
    ],
    { stdio: "inherit" },
  );
  assert.equal(
    inspectImage(`${image}:${version}`),
    digest,
    "Promotion changed the candidate digest",
  );
}

export async function publishRelease(manifest, services, results) {
  if (process.env.GITHUB_ACTIONS !== "true") throw new Error("Release publication is CI-only.");
  assert.equal(git("rev-parse", "HEAD"), manifest.sourceSha, "Publication source mismatch");
  const { auto } = await createPlatformAuto({
    branch: manifest.branch,
    sourceSha: manifest.sourceSha,
  });
  const completed = await completeManifest(manifest, services, results, inspectImage, promoteImage);
  const repository = process.env.GITHUB_REPOSITORY;
  const [owner, repo] = repository.split("/");
  const commits = await auto.release.getCommitsInRelease(
    manifest.platform.from,
    manifest.sourceSha,
  );
  const eligible = await notificationPrs(auto, commits, manifest.platform.channel !== "stable");
  // Keep Auto's native commenter/label transition, supplying only PRs which
  // have not already been notified for this phase (including across channels).
  installNotificationFilter(auto, eligible);
  auto.hooks.makeRelease.tapPromise(
    { name: "Teck platform manifest", stage: -1 },
    async (context) => {
      const notes = platformNotes(manifest, manifest.groupNotes, context.fullReleaseNotes);
      for (const unit of completed.components.filter((component) => component.changed)) {
        await ensureGitTag(auto.git.github, repository, unit.tag, manifest.sourceSha);
      }
      await ensureGitTag(auto.git.github, repository, manifest.platform.tag, manifest.sourceSha, {
        createIfMissing: false,
      });
      let release;
      try {
        release = await auto.git.github.repos.getReleaseByTag({
          owner,
          repo,
          tag: manifest.platform.tag,
        });
        assert.equal(release.data.prerelease, manifest.platform.channel !== "stable");
      } catch (error) {
        if (error.status !== 404) throw error;
        release = await auto.git.publish(
          githubReleaseBody(manifest, notes),
          manifest.platform.tag,
          manifest.platform.channel !== "stable",
          manifest.sourceSha,
          manifest.platform.channel === "stable",
        );
      }
      const assets = await auto.git.github.repos.listReleaseAssets({
        owner,
        repo,
        release_id: release.data.id,
      });
      for (const [name, contents, contentType] of [
        ["platform-manifest.json", `${JSON.stringify(completed, null, 2)}\n`, "application/json"],
        ["release-notes.md", notes, "text/markdown"],
      ]) {
        if (assets.data.some((asset) => asset.name === name)) continue;
        const data = Buffer.from(contents);
        await auto.git.github.repos.uploadReleaseAsset({
          owner,
          repo,
          release_id: release.data.id,
          name,
          data,
          headers: { "content-type": contentType, "content-length": data.length },
        });
      }
      return release;
    },
  );
  await auto.createLabels();
  await auto.runRelease({
    from: manifest.platform.from,
    to: manifest.sourceSha,
    useVersion: manifest.platform.version,
    prerelease: manifest.platform.channel !== "stable",
  });
  writeFileSync(".nx/releases/platform-manifest.json", `${JSON.stringify(completed, null, 2)}\n`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const resultsDirectory = process.argv[2] ?? ".nx/releases/images";
  const results = readdirSync(resultsDirectory)
    .filter((name) => name.startsWith("image-") && name.endsWith(".json"))
    .map((name) => JSON.parse(readFileSync(`${resultsDirectory}/${name}`, "utf8")));
  await publishRelease(
    readManifest(),
    JSON.parse(readFileSync(".nx/releases/services.json", "utf8")),
    results,
  );
}

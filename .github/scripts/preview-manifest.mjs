import assert from "node:assert/strict";
import { readdirSync, readFileSync, writeFileSync } from "node:fs";
import { pathToFileURL } from "node:url";
import { channelFor, git, selectPlatformTag } from "./platform-release.mjs";

export function previewManifest({
  number,
  baseSha,
  headSha,
  sourceSha,
  baselinePlatformTag,
  images,
}) {
  assert.ok(Number.isInteger(Number(number)) && Number(number) > 0);
  for (const sha of [baseSha, headSha, sourceSha]) assert.match(sha, /^[a-f0-9]{40}$/);
  for (const image of images) {
    assert.equal(image.sourceSha, sourceSha, "Preview must use the tested merge commit");
    assert.match(image.digest, /^sha256:[a-f0-9]{64}$/);
  }
  return {
    schemaVersion: 1,
    kind: "preview",
    pullRequest: Number(number),
    baseSha,
    headSha,
    sourceSha,
    baselinePlatformTag,
    images: images.map((image) => ({
      image: image.image,
      tag: image.tag,
      digest: image.digest,
      ref: `${image.image}@${image.digest}`,
    })),
  };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const directory = process.argv[2] ?? ".nx/previews/images";
  const images = readdirSync(directory)
    .filter((name) => name.startsWith("image-") && name.endsWith(".json"))
    .map((name) => JSON.parse(readFileSync(`${directory}/${name}`, "utf8")));
  const tags = git("tag", "--merged", process.env.PR_BASE_SHA, "--list", "v*").split(/\r?\n/);
  const baselinePlatformTag =
    selectPlatformTag(tags, channelFor(process.env.PR_BASE_REF)) ?? selectPlatformTag(tags);
  const manifest = previewManifest({
    number: process.env.PR_NUMBER,
    baseSha: process.env.PR_BASE_SHA,
    headSha: process.env.PR_HEAD_SHA,
    sourceSha: process.env.PREVIEW_SOURCE_SHA,
    baselinePlatformTag,
    images,
  });
  writeFileSync(".nx/previews/preview-manifest.json", `${JSON.stringify(manifest, null, 2)}\n`);
}

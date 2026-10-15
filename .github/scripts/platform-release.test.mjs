import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import test from "node:test";
import autoCore from "@auto-it/core";
import { AsyncSeriesBailHook, AsyncSeriesWaterfallHook } from "tapable";
import { ComponentChangelogRenderer, groupChangelogRenderer } from "./group-changelog.mjs";
import {
  channelFor,
  createPlatformAuto,
  git,
  githubReleaseBody,
  platformNotes,
  platformVersion,
  releaseChecksPassed,
  releaseSourceAlreadyPublished,
  selectPlatformTag,
} from "./platform-release.mjs";
import { previewManifest } from "./preview-manifest.mjs";
import {
  completeManifest,
  ensureGitTag,
  installNotificationFilter,
  notificationPrs,
  publishRelease,
} from "./publish-release.mjs";
import { releaseServices } from "./release-matrix.mjs";
import { acceptSummary, applySummary, generateSummary, summaryInput } from "./release-summary.mjs";

const require = createRequire(import.meta.url);
const { Auto } = autoCore;
const Released = require("@auto-it/released").default;
const Conventional = require("@auto-it/conventional-commits").default;
const { defaultLabels } = require("@auto-it/core/dist/semver");
const { getVersionMap } = require("@auto-it/core/dist/release");
const {
  DEFAULT_CONVENTIONAL_COMMITS_CONFIG,
} = require("nx/src/command-line/release/config/conventional-commits");
const sha = "a".repeat(40);
const digest = `sha256:${"b".repeat(64)}`;
const otherDigest = `sha256:${"c".repeat(64)}`;
const config = JSON.parse(readFileSync(".autorc.json", "utf8"));

test("release and validation workflows pin external actions to exact commit hashes", () => {
  for (const name of [
    "release",
    "canary-preview",
    "reusable-build-sign-sbom",
    "ci",
    "security-scans",
  ]) {
    const workflow = readFileSync(`.github/workflows/${name}.yml`, "utf8");
    for (const [, reference] of workflow.matchAll(/^\s+(?:-\s+)?uses:\s+([^\s#]+)/gm)) {
      if (reference.startsWith("./") || reference.startsWith("$/")) continue;
      assert.match(reference, /@[a-f0-9]{40}$/, `${name}: invalid action pin ${reference}`);
    }
  }
});

function nativeAuto() {
  const auto = new Auto({ quiet: true, baseBranch: "main", disableTsNode: true });
  auto.config = { ...config, labels: [...defaultLabels] };
  auto.semVerLabels = getVersionMap(auto.config.labels);
  return auto;
}
test("the actual Auto configuration initializes the pinned native plugins", async (t) => {
  const previousToken = process.env.GH_TOKEN;
  process.env.GH_TOKEN = "offline-test";
  t.after(() => {
    if (previousToken === undefined) delete process.env.GH_TOKEN;
    else process.env.GH_TOKEN = previousToken;
  });
  t.mock.method(Auto.prototype, "getRemote", async () => "https://example.test/repository.git");
  t.mock.method(process, "exit", (code) => {
    throw new Error(`Auto initialization exited with ${code}`);
  });
  const { auto } = await createPlatformAuto({
    branch: "next",
    sourceSha: git(
      "rev-parse",
      selectPlatformTag(git("tag", "--list", "v*").split(/\r?\n/)) ?? "HEAD",
    ),
  });
  assert.equal(auto.git.options.token, "offline-test");
  assert.equal(auto.inPrereleaseBranch(), true);
  assert.ok(auto.hooks.afterRelease.taps.some((tap) => tap.name === "released"));
  assert.ok(auto.hooks.next.taps.some((tap) => tap.name === "git-tag"));
  const parser = {
    hooks: {
      parseCommit: new AsyncSeriesWaterfallHook(["commit"]),
      omitCommit: new AsyncSeriesBailHook(["commit"]),
    },
  };
  auto.hooks.onCreateLogParse.call(parser);
  const commit = await parser.hooks.parseCommit.promise({
    subject: "feat(order): add checkout",
    rawBody: "",
    hash: sha,
    labels: [],
  });
  assert.ok(commit.labels.includes("minor"));
  auto.git.getProject = async () => ({ html_url: "https://example.test/repository" });
  const changelog = await auto.release.makeChangelog();
  assert.match(
    await changelog.hooks.renderChangelogTitle.promise("pushToBaseBranch", {}),
    /Pushed to `next`/,
  );
});
test("branches select the production or prerelease channel", () => {
  assert.equal(channelFor("main"), "stable");
  for (const channel of ["alpha", "beta", "next"]) assert.equal(channelFor(channel), channel);
  assert.throws(() => channelFor("feature/checkout"));
});
test("platform tags exclude group versions and other prerelease channels", () => {
  const tags = [
    "v1.2.0",
    "v1.2.1",
    "order@v9.0.0",
    "v1.3.0-alpha.2",
    "v1.3.0-beta.0",
    "v1.3.0-next.1",
    "canary/order@v1.4.0",
  ];
  assert.equal(selectPlatformTag(tags), "v1.2.1");
  assert.equal(selectPlatformTag(tags, "alpha"), "v1.3.0-alpha.2");
  assert.equal(selectPlatformTag(tags, "next"), "v1.3.0-next.1");
});
test("repeated source commits skip publication while new sources and stable promotion remain eligible", () => {
  const tags = { "v1.2.0": "old-source", "v1.3.0-next.0": sha, "v1.3.0": sha };
  const resolve = (tag) => tags[tag];
  const published = (stableTag, candidateTag = null, sourceSha = sha) =>
    releaseSourceAlreadyPublished({ sourceSha, stableTag, candidateTag }, resolve);
  assert.equal(published(null), false, "first release");
  assert.equal(published("v1.3.0"), true, "duplicate stable trigger");
  assert.equal(published("v1.2.0", "v1.3.0-next.0"), true, "duplicate candidate trigger");
  assert.equal(published("v1.2.0", "v1.3.0-next.0", "new-source"), false, "new candidate");
  assert.equal(published("v1.3.0", null, "new-source"), false, "new stable source");
  assert.equal(published("v1.2.0"), false, "promotion from next to stable");
});
test("native Auto increments candidates and stable releases without repeated base bumps", () => {
  assert.equal(
    platformVersion({
      channel: "stable",
      stableTag: "v1.2.0",
      bump: "patch",
    }),
    "1.2.1",
  );
  for (const channel of ["alpha", "beta", "next"]) {
    assert.equal(
      platformVersion({ channel, stableTag: "v1.2.0", bump: "minor" }),
      `1.3.0-${channel}.0`,
    );
    assert.equal(
      platformVersion({
        channel,
        stableTag: "v1.2.0",
        candidateTag: `v1.3.0-${channel}.0`,
        bump: "minor",
      }),
      `1.3.0-${channel}.1`,
    );
  }
  assert.equal(platformVersion({ channel: "stable", stableTag: "v1.2.0", bump: "minor" }), "1.3.0");
  assert.equal(
    platformVersion({
      channel: "next",
      stableTag: "v1.2.1",
      candidateTag: "v1.3.0-next.1",
      bump: "major",
    }),
    "2.0.0-next.0",
  );
  assert.equal(platformVersion({ channel: "stable", bump: "" }), null);
});
test("Auto's actual Conventional Commit plugin recognizes features, fixes and breaking changes", async () => {
  const auto = nativeAuto();
  auto.git = {};
  new Conventional(
    config.plugins.find(
      (plugin) => Array.isArray(plugin) && plugin[0] === "conventional-commits",
    )[1],
  ).apply(auto);
  const parser = {
    hooks: {
      parseCommit: new AsyncSeriesWaterfallHook(["commit"]),
      omitCommit: new AsyncSeriesBailHook(["commit"]),
    },
  };
  auto.hooks.onCreateLogParse.call(parser);
  for (const [subject, expected] of [
    ["feat(order): add checkout", "minor"],
    ["fix(order): repair checkout", "patch"],
    ["feat(order)!: change checkout", "major"],
    ["docs(repo): clarify workflow", "patch"],
    ["chore(deps): update dependencies", "patch"],
    ["perf(order): speed up checkout", "patch"],
  ]) {
    const commit = await parser.hooks.parseCommit.promise({
      subject,
      rawBody: "",
      hash: sha,
      labels: [],
    });
    assert.ok(commit.labels.includes(expected), `${subject}: ${commit.labels}`);
  }
});
test("release gates require the latest successful push runs for the exact branch and SHA", () => {
  const runs = ["CI", "Security Scans"].map((name, id) => ({
    name,
    id,
    run_attempt: 1,
    head_sha: sha,
    head_branch: "next",
    event: "push",
    status: "completed",
    conclusion: "success",
  }));
  assert.equal(releaseChecksPassed(runs, sha, "next"), true);
  assert.equal(releaseChecksPassed(runs, sha, "main"), false);
  assert.equal(releaseChecksPassed(runs.slice(0, 1), sha, "next"), false);
  assert.equal(
    releaseChecksPassed([...runs, { ...runs[0], id: 3, conclusion: "failure" }], sha, "next"),
    false,
  );
  assert.equal(
    releaseChecksPassed(
      runs.map((run) => ({ ...run, event: "pull_request" })),
      sha,
      "next",
    ),
    false,
  );
});

const services = [
  { product: "order", releaseGroup: "order", group: "commerce", service: "order" },
  { product: "catalog", releaseGroup: "catalog", group: "commerce", service: "catalog" },
];
function plan() {
  return {
    sourceSha: sha,
    platform: {
      version: "1.3.0-next.1",
      tag: "v1.3.0-next.1",
      channel: "next",
      from: "v1.3.0-next.0",
      candidateTag: "v1.3.0-next.0",
      stableTag: "v1.2.0",
    },
    components: [
      { name: "order", releaseGroup: "order", version: "2.1.0-next.0", changed: true, images: [] },
      { name: "catalog", releaseGroup: "catalog", version: "3.0.0", changed: false, images: [] },
    ],
  };
}
function imageResult() {
  return {
    image: "ghcr.io/teck-lab/teck-monorepo/commerce/order",
    sourceSha: sha,
    artifactVersion: "2.1.0-next.0",
    version: `2.1.0-next.0-build.${sha}`,
    digest,
  };
}
test("only changed services enter the build matrix", () => {
  assert.deepEqual(
    releaseServices(plan(), services).map((service) => service.product),
    ["order"],
  );
  assert.throws(() => releaseServices(plan(), [{ product: "unknown", releaseGroup: "unknown" }]));
});
test("the complete manifest retains unchanged component versions and exact image digests", async () => {
  const promoted = [];
  const manifest = await completeManifest(
    plan(),
    services,
    [imageResult()],
    async (ref) => (ref.includes("/order:") ? digest : otherDigest),
    async (...args) => promoted.push(args),
  );
  assert.equal(promoted.length, 1);
  assert.equal(manifest.components[1].version, "3.0.0");
  assert.equal(manifest.components[1].images[0].digest, otherDigest);
  assert.equal(manifest.components[0].images[0].digest, digest);
  assert.equal(plan().components[0].images.length, 0);
});
test("publication rejects missing, mismatched-source and mismatched-digest candidates", async () => {
  await assert.rejects(
    completeManifest(
      plan(),
      services,
      [],
      async () => digest,
      async () => {},
    ),
  );
  await assert.rejects(
    completeManifest(
      plan(),
      services,
      [{ ...imageResult(), sourceSha: "c".repeat(40) }],
      async () => digest,
      async () => {},
    ),
  );
  await assert.rejects(
    completeManifest(
      plan(),
      services,
      [imageResult()],
      async () => otherDigest,
      async () => {},
    ),
  );
});
test("all image digests must validate before any final image tag is promoted", async () => {
  const promoted = [];
  await assert.rejects(
    completeManifest(
      plan(),
      services,
      [imageResult()],
      async (ref) => (ref.includes("/order:") ? digest : "invalid"),
      async (...args) => promoted.push(args),
    ),
  );
  assert.equal(promoted.length, 0);
});
test("component tags can be retried but never overwritten", async () => {
  let created = false;
  const github = {
    git: {
      getRef: async () => ({ data: { object: { type: "commit", sha } } }),
      createRef: async () => {
        created = true;
      },
    },
  };
  await ensureGitTag(github, "Teck-Lab/Teck.Monorepo", "order@v1.0.0", sha);
  assert.equal(created, false);
  await assert.rejects(
    ensureGitTag(github, "Teck-Lab/Teck.Monorepo", "order@v1.0.0", "c".repeat(40)),
  );
});
test("native Auto notifies each PR once for prereleases and transitions all PRs at stable publication", async () => {
  const labels = new Map([
    [1, []],
    [2, []],
  ]);
  const comments = [];
  const commits = [1, 2].map((number) => ({
    subject: "feat(order): add checkout",
    labels: ["minor"],
    authors: [],
    pullRequest: { number },
  }));
  async function notify(numbers, prerelease, version) {
    const auto = nativeAuto();
    auto.git = {
      getLabels: async (number) => labels.get(number),
      getPullRequest: async () => ({ data: { head: { ref: "feature/checkout" }, body: "" } }),
      getCommitsForPR: async () => [],
      createComment: async (message, number) => comments.push({ number, message }),
      addLabelToPr: async (number, label) => labels.get(number).push(label),
      removeLabel: async (number, label) =>
        labels.set(
          number,
          labels.get(number).filter((existing) => existing !== label),
        ),
    };
    new Released({ label: "released", prereleaseLabel: "pre-released", includeBotPrs: true }).apply(
      auto,
    );
    const selected = commits.filter((commit) => numbers.includes(commit.pullRequest.number));
    installNotificationFilter(auto, await notificationPrs(auto, selected, prerelease));
    await auto.hooks.afterRelease.promise({
      newVersion: version,
      commits: selected,
      response: {
        data: { prerelease, name: version, html_url: `https://example.test/${version}` },
      },
    });
  }
  await notify([1], true, "1.3.0-alpha.0");
  await notify([1, 2], true, "1.3.0-beta.0");
  await notify([1, 2], true, "1.3.0-next.0");
  assert.deepEqual(
    comments.map((comment) => comment.number),
    [1, 2],
  );
  await notify([1, 2], false, "1.3.0");
  assert.deepEqual(
    comments.map((comment) => comment.number),
    [1, 2, 1, 2],
  );
  for (const current of labels.values()) assert.deepEqual(current, ["released"]);
  await notify([1, 2], false, "1.3.0");
  assert.equal(comments.length, 4);
});
test("preview manifests contain build references rather than release versions", () => {
  const manifest = previewManifest({
    number: 123,
    baseSha: sha,
    headSha: sha,
    sourceSha: sha,
    baselinePlatformTag: "v1.2.0",
    images: [{ image: "ghcr.io/test/order", tag: "pr-123-merge", digest, sourceSha: sha }],
  });
  assert.equal(manifest.kind, "preview");
  assert.equal(manifest.platform, undefined);
  assert.equal(manifest.images[0].ref, `ghcr.io/test/order@${digest}`);
  assert.throws(() =>
    previewManifest({
      number: 123,
      baseSha: sha,
      headSha: sha,
      sourceSha: sha,
      images: [{ image: "test", digest, sourceSha: "c".repeat(40) }],
    }),
  );
});
test("release notes include versions and changes only for changed components", () => {
  const notes = platformNotes(plan(), { order: "- checkout (#1)" }, "- platform changes");
  assert.doesNotMatch(notes, /catalog · v3\.0\.0/);
  assert.match(notes, /## 📦 order · v2\.1\.0-next\.0\n\n- checkout/);
  assert.match(notes, /updates 1 of 2 components: order/);
  assert.doesNotMatch(notes, /\| Component \| Version \|/);
});
test("release changelogs omit unchanged components while retaining versioned headers", () => {
  const notes = platformNotes(plan(), { order: "- checkout (#1)" }, "- platform changes");
  const body = githubReleaseBody(plan(), notes);
  assert.match(body.split("<details>")[0], /Highlights: checkout/);
  assert.match(body, /<summary><h2>📦 order · v2\.1\.0-next\.0<\/h2><\/summary>/);
  assert.doesNotMatch(body, /catalog · v3\.0\.0/);
  assert.match(body, /<summary><h2>🛠️ Platform changes<\/h2><\/summary>/);
  assert.equal(body.match(/📦 order · v2\.1\.0-next\.0/g)?.length, 1);
  assert.equal(body.match(/🛠️ Platform changes/g)?.length, 1);
  assert.doesNotMatch(body, /^## (?:📦|🛠️)/m);
  assert.doesNotMatch(body, /<details open|\| Component \| Version \|/);
  assert.equal(body.match(/<details>/g)?.length, 2);
  assert.equal(body.match(/<\/details>/g)?.length, 2);
  assert.doesNotMatch(notes, /<details>/, "the full attachment stays plain Markdown");
});
test("platform-only releases keep the compare link and complete manifest without empty component sections", () => {
  const manifest = plan();
  for (const unit of manifest.components) unit.changed = false;
  const original = structuredClone(manifest);
  const body = githubReleaseBody(
    manifest,
    platformNotes(manifest, {}, "- improve release automation"),
  );
  assert.doesNotMatch(body, /🧩 Components|<summary><h2>📦|catalog ·|order ·/);
  assert.match(body, /improve release automation/);
  assert.match(body, /\/compare\/v1\.3\.0-next\.0\.\.\.v1\.3\.0-next\.1/);
  assert.equal(body.match(/<details>/g)?.length, 1);
  assert.deepEqual(manifest, original);
});
test("fixed-group notes include each change once across member projects using Nx's renderer", async () => {
  const Renderer = groupChangelogRenderer(["Order.Application", "Order.Host"]);
  const changes = [
    {
      type: "feat",
      scope: "order",
      description: "add checkout",
      affectedProjects: ["Order.Application", "Order.Host"],
    },
    {
      type: "fix",
      scope: "order",
      description: "repair checkout",
      affectedProjects: ["Order.Host"],
    },
    {
      type: "fix",
      scope: "catalog",
      description: "repair catalog",
      affectedProjects: ["Catalog.Host"],
    },
  ];
  const entries = await Promise.all(
    ["Order.Application", "Order.Host"].map((project) =>
      new Renderer({
        project,
        changes,
        changelogEntryVersion: "1.2.0",
        entryWhenNoChanges: false,
        isVersionPlans: false,
        changelogRenderOptions: { authors: false, commitReferences: false, versionTitleDate: true },
        conventionalCommitsConfig: DEFAULT_CONVENTIONAL_COMMITS_CONFIG,
        remoteReleaseClient: { getRemoteRepoData: () => null },
      }).render(),
    ),
  );
  assert.equal(entries[0], entries[1]);
  assert.equal(entries[0].match(/add checkout/g)?.length, 1);
  assert.match(entries[0], /repair checkout/);
  assert.doesNotMatch(entries[0], /repair catalog/);
  assert.doesNotMatch(entries[0], /^#{1,2} 1\.2\.0|\(\d{4}-\d{2}-\d{2}\)/m);
});

test("real Nx component notes retain changes without a second version/date or section heading", async () => {
  for (const version of ["1.0.0", "2.1.0", "2.1.0-next.0"]) {
    const notes = await new ComponentChangelogRenderer({
      project: "mobile",
      changes: [
        {
          type: "fix",
          scope: "mobile",
          description: "align NativeWind RC",
          affectedProjects: ["mobile"],
        },
      ],
      changelogEntryVersion: version,
      entryWhenNoChanges: false,
      isVersionPlans: false,
      changelogRenderOptions: { authors: false, commitReferences: false, versionTitleDate: true },
      conventionalCommitsConfig: DEFAULT_CONVENTIONAL_COMMITS_CONFIG,
      remoteReleaseClient: { getRemoteRepoData: () => null },
    }).render();
    const manifest = plan();
    manifest.components = [{ name: "mobile", version, changed: true }];
    const attachment = platformNotes(manifest, { mobile: notes }, "- platform maintenance");
    const body = githubReleaseBody(manifest, attachment);
    assert.equal(body.split(`📦 mobile · v${version}`).length - 1, 1);
    assert.ok(body.includes(`<summary><h2>📦 mobile · v${version}</h2></summary>`));
    assert.match(body, /<\/summary>\n\n### 🩹 Fixes\n\n- \*\*mobile:\*\* align NativeWind RC/);
    assert.doesNotMatch(body, /^#{1,2} (?:📦|\d)/m);
    assert.doesNotMatch(attachment, /^#{1,2} \d|\(\d{4}-\d{2}-\d{2}\)/m);
    assert.ok(attachment.includes(`## 📦 mobile · v${version}\n\n### 🩹 Fixes`));
  }
});

test("dependency-only and version-only Nx notes keep their explanation without version/date boilerplate", async () => {
  for (const dependencyBumps of [
    undefined,
    [{ dependencyName: "ui-native", newVersion: "1.2.0" }],
  ]) {
    const notes = await new ComponentChangelogRenderer({
      project: "mobile",
      changes: [],
      dependencyBumps,
      changelogEntryVersion: "2.1.0",
      entryWhenNoChanges: "Dependency-driven version update.",
      isVersionPlans: false,
      changelogRenderOptions: { authors: false, versionTitleDate: true },
      conventionalCommitsConfig: DEFAULT_CONVENTIONAL_COMMITS_CONFIG,
      remoteReleaseClient: { getRemoteRepoData: () => null },
    }).render();
    assert.doesNotMatch(notes, /^#{1,2} \d|\(\d{4}-\d{2}-\d{2}\)/m);
    assert.match(notes, dependencyBumps ? /Updated ui-native to 1\.2\.0/ : /Dependency-driven/);
  }
});
test("oversized platform histories retain inline component changes and the full attachment", () => {
  const notes = platformNotes(plan(), { order: "- checkout" }, "x".repeat(130_000));
  const body = githubReleaseBody(plan(), notes);
  assert.ok(body.length < 120_000);
  assert.match(body, /release-notes\.md/);
  assert.doesNotMatch(body, /catalog · v3\.0\.0/);
  assert.match(body, /<summary><h2>📦 order · v2\.1\.0-next\.0<\/h2><\/summary>\n\n- checkout/);
  assert.match(body, /<summary><h2>🛠️ Platform changes<\/h2><\/summary>/);
  assert.equal(githubReleaseBody(plan(), "small release"), "small release");
});
test("oversized histories keep complete linked entries for every changed component", () => {
  const manifest = plan();
  manifest.components = Array.from({ length: 13 }, (_, index) => ({
    name: `component-${index}`,
    version: "0.0.1",
    changed: true,
  }));
  const entry = "- add checkout ([#123](https://github.com/test/repo/pull/123))\n";
  const groups = Object.fromEntries(
    manifest.components.map((unit) => [unit.name, `### Features\n\n${entry.repeat(1_000)}`]),
  );
  const body = githubReleaseBody(manifest, platformNotes(manifest, groups, "- repair CI"));
  assert.ok(Buffer.byteLength(body) <= 120_000);
  for (const unit of manifest.components) {
    assert.ok(
      body.includes(
        `<summary><h2>📦 ${unit.name} · v0.0.1</h2></summary>\n\n### Features\n\n${entry.trimEnd()}`,
      ),
    );
    assert.equal(body.split(`📦 ${unit.name} · v0.0.1`).length - 1, 1);
  }
  assert.match(body, /<summary><h2>🛠️ Platform changes<\/h2><\/summary>\n\n- repair CI/);
  const collapsed = body.slice(body.indexOf("<details>"));
  assert.equal(
    collapsed.match(/add checkout/g)?.length,
    collapsed.match(/\/pull\/123\)\)/g)?.length,
  );
  assert.match(body, /Additional changes are listed/);
  assert.equal(body.match(/<details>/g)?.length, 14);
  assert.equal(body.match(/<\/details>/g)?.length, 14);
});
test("release bodies budget multibyte notes by UTF-8 size and preserve small releases", () => {
  const entry = "- checkout 🛒 æøå ([#123](https://github.com/test/repo/pull/123))\n";
  const small = platformNotes(plan(), { order: entry }, "- repair CI");
  const smallBody = githubReleaseBody(plan(), small);
  assert.ok(smallBody.includes(entry.trimEnd()));
  assert.match(smallBody, /<summary><h2>🛠️ Platform changes<\/h2><\/summary>\n\n- repair CI/);
  assert.doesNotMatch(smallBody, /shortened|Additional changes are listed/);
  const notes = platformNotes(
    plan(),
    { order: entry.repeat(Math.ceil(120_000 / Buffer.byteLength(entry))) },
    "- repair CI",
  );
  assert.ok(notes.length < 120_000);
  assert.ok(Buffer.byteLength(notes) > 120_000);
  const body = githubReleaseBody(plan(), notes);
  assert.ok(Buffer.byteLength(body) <= 120_000);
  assert.match(body, /checkout 🛒 æøå/);
  assert.doesNotMatch(body, /\uFFFD/);
  assert.match(body, /<summary><h2>🛠️ Platform changes<\/h2><\/summary>\n\n- repair CI/);
});
test("comparison links use the release baseline and remain below all shortened sections", () => {
  const manifest = plan();
  const body = githubReleaseBody(
    manifest,
    platformNotes(manifest, { order: "- checkout\n".repeat(20_000) }, "- repair CI"),
  );
  const footer = body.slice(body.lastIndexOf("</details>") + "</details>".length);
  assert.match(
    footer,
    /^\n\n---\n\n/,
    "the footer must leave the HTML block before Markdown resumes",
  );
  assert.match(footer, /\/compare\/v1\.3\.0-next\.0\.\.\.v1\.3\.0-next\.1/);
  manifest.platform = {
    version: "1.3.0",
    tag: "v1.3.0",
    channel: "stable",
    from: "v1.2.0",
    stableTag: "v1.2.0",
  };
  assert.match(platformNotes(manifest, {}, ""), /\/compare\/v1\.2\.0\.\.\.v1\.3\.0/);
  manifest.platform = { version: "0.1.0", channel: "stable", from: sha };
  assert.match(platformNotes(manifest, {}, ""), /Compare changes from the initial commit/);
});
test("AI overview inputs include bounded, directly scoped changes for every changed component", () => {
  const manifest = plan();
  manifest.groupNotes = {
    order: `${"- general maintenance\n".repeat(10_000)}- **order:** add checkout\n`,
  };
  const input = summaryInput(manifest);
  const data = JSON.parse(input);
  assert.ok(Buffer.byteLength(input) < 60_000);
  assert.equal(data.components.length, 1);
  assert.equal(data.components[0].changes.length, 8);
  assert.equal(data.components[0].changes[0], "order: add checkout");
});
test("OmniRoute receives OpenAI-compatible requests targeting the configured combo", async () => {
  const manifest = plan();
  const summary = "This platform release improves checkout and repairs the release automation.";
  const record = await generateSummary(manifest, {
    baseUrl: "https://route.example/v1/",
    model: "teck-fast-text",
    apiKey: "test-key",
    request: async (url, options) => {
      assert.equal(url.href, "https://route.example/v1/chat/completions");
      assert.equal(options.headers.authorization, "Bearer test-key");
      const payload = JSON.parse(options.body);
      assert.equal(payload.model, "teck-fast-text");
      assert.equal(payload.messages[0].role, "system");
      assert.equal(payload.tools, undefined);
      assert.equal(JSON.parse(payload.messages[1].content).version, manifest.platform.version);
      return { ok: true, json: async () => ({ choices: [{ message: { content: summary } }] }) };
    },
  });
  assert.equal(applySummary(manifest, record).releaseSummary, summary);
  assert.match(platformNotes(applySummary(manifest, record), {}, ""), new RegExp(summary));
});
test("AI overview validation rejects wrong sources, malformed output and provider error bodies", async () => {
  const manifest = plan();
  const record = acceptSummary(
    manifest,
    "This release improves checkout and fixes release preparation.",
  );
  assert.throws(
    () => applySummary(manifest, { ...record, sourceSha: "different-source" }),
    /source mismatch/,
  );
  assert.throws(() => applySummary(manifest, { ...record, version: "9.0.0" }), /version mismatch/);
  assert.throws(() => acceptSummary(manifest, `<details>${"bad ".repeat(20)}`), /without HTML/);
  assert.throws(() => acceptSummary(manifest, undefined), /Missing overview/);
  await assert.rejects(
    generateSummary(manifest, {
      baseUrl: "https://route.example/v1",
      model: "teck-fast-text",
      apiKey: "test-key",
      request: async () => ({
        ok: false,
        status: 429,
        json: () => {
          throw new Error("Provider details must not be logged");
        },
      }),
    }),
    /HTTP 429/,
  );
});
test("release publication is refused outside CI", async () => {
  if (process.env.GITHUB_ACTIONS === "true") return;
  await assert.rejects(publishRelease({}, [], []), /CI-only/);
});

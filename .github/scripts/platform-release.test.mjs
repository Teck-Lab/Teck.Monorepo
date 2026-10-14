import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import test from "node:test";
import autoCore from "@auto-it/core";
import { AsyncSeriesBailHook, AsyncSeriesWaterfallHook } from "tapable";
import { groupChangelogRenderer } from "./group-changelog.mjs";
import {
  channelFor,
  createPlatformAuto,
  git,
  githubReleaseBody,
  platformNotes,
  platformVersion,
  releaseChecksPassed,
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
    platform: { version: "1.3.0-next.1", channel: "next" },
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
test("release notes include complete versions and group-scoped change sections", () => {
  const notes = platformNotes(plan(), { order: "- checkout (#1)" }, "- platform changes");
  assert.match(notes, /catalog \| 3\.0\.0/);
  assert.match(notes, /## order\n\n- checkout/);
  assert.doesNotMatch(notes, /## catalog/);
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
        changelogRenderOptions: { authors: false, commitReferences: false },
        conventionalCommitsConfig: DEFAULT_CONVENTIONAL_COMMITS_CONFIG,
        remoteReleaseClient: { getRemoteRepoData: () => null },
      }).render(),
    ),
  );
  assert.equal(entries[0], entries[1]);
  assert.equal(entries[0].match(/add checkout/g)?.length, 1);
  assert.match(entries[0], /repair checkout/);
  assert.doesNotMatch(entries[0], /repair catalog/);
});
test("oversized histories retain the version table and reference complete attached notes", () => {
  const notes = platformNotes(plan(), { order: "- checkout" }, "x".repeat(130_000));
  const body = githubReleaseBody(plan(), notes);
  assert.ok(body.length < 120_000);
  assert.match(body, /release-notes\.md/);
  assert.match(body, /catalog \| 3\.0\.0/);
  assert.equal(githubReleaseBody(plan(), "small release"), "small release");
});
test("release publication is refused outside CI", async () => {
  if (process.env.GITHUB_ACTIONS === "true") return;
  await assert.rejects(publishRelease({}, [], []), /CI-only/);
});

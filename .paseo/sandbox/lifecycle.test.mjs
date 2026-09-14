import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { cpSync, mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { pathToFileURL } from "node:url";
import test from "node:test";
import { shouldRemoveFailedSandbox, validateWorkspace } from "./lifecycle.mjs";

const ompFiles = ["config.yml", "models.yml", "RULES.md", "mcp.json", "lsp.json"];

function cleanGitEnvironment() {
  const env = { ...process.env };
  delete env.GIT_DIR;
  delete env.GIT_WORK_TREE;
  return env;
}

function initializeGit(root) {
  const result = spawnSync("git", ["init", "--quiet", root], {
    encoding: "utf8",
    env: cleanGitEnvironment(),
  });
  assert.equal(result.status, 0, result.stderr);
}

function fixture(t, { git = true, config = "{}" } = {}) {
  const root = mkdtempSync(join(tmpdir(), "paseo-sandbox-"));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  if (git) initializeGit(root);
  mkdirSync(join(root, ".paseo", "sandbox", "kit"), { recursive: true });
  mkdirSync(join(root, ".omp"), { recursive: true });
  writeFileSync(join(root, ".paseo", "sandbox", "config.json"), config);
  writeFileSync(join(root, ".paseo", "sandbox", "kit", "spec.yaml"), "schemaVersion: \"2\"\n");
  for (const file of ompFiles) writeFileSync(join(root, ".omp", file), "\n");
  return root;
}

test("accepts an initialized Git repository without package.json", (t) => {
  assert.doesNotThrow(() => validateWorkspace(fixture(t)));
});

test("rejects a non-Git directory", (t) => {
  assert.throws(() => validateWorkspace(fixture(t, { git: false })), /git/i);
});

test("reports missing sandbox prerequisites", (t) => {
  const root = fixture(t);
  rmSync(join(root, ".paseo", "sandbox", "kit", "spec.yaml"));
  rmSync(join(root, ".omp", "mcp.json"));
  assert.throws(() => validateWorkspace(root), (error) => {
    assert.match(error.message, /spec\.yaml/);
    assert.match(error.message, /mcp\.json/);
    return true;
  });
});

test("rejects a non-object sandbox config", (t) => {
  assert.throws(() => validateWorkspace(fixture(t, { config: "[]" })), /Invalid sandbox config/);
});

test("copyable template validates without package.json", async (t) => {
  const root = mkdtempSync(join(tmpdir(), "paseo-template-"));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  const template = new URL("../../tools/paseo-sandbox-template/template/", import.meta.url);
  cpSync(template, root, { recursive: true });
  initializeGit(root);
  const lifecyclePath = join(root, ".paseo", "sandbox", "lifecycle.mjs");
  const module = await import(`${pathToFileURL(lifecyclePath).href}?fixture=${Date.now()}`);
  assert.doesNotThrow(() => module.validateWorkspace(root));
  assert.equal(
    readFileSync(lifecyclePath, "utf8"),
    readFileSync(new URL("./lifecycle.mjs", import.meta.url), "utf8"),
  );
});

test("failed sandbox cleanup only removes a newly created sandbox", () => {
  assert.equal(shouldRemoveFailedSandbox(true, {}), true);
  assert.equal(shouldRemoveFailedSandbox(false, {}), false);
  assert.equal(shouldRemoveFailedSandbox(true, { PASEO_SANDBOX_KEEP_FAILED: "1" }), false);
  assert.equal(shouldRemoveFailedSandbox(true, { TECK_PASEO_KEEP_FAILED: "1" }), false);
  assert.equal(
    shouldRemoveFailedSandbox(true, {
      PASEO_SANDBOX_KEEP_FAILED: "0",
      TECK_PASEO_KEEP_FAILED: "1",
    }),
    true,
  );
  assert.equal(
    shouldRemoveFailedSandbox(true, {
      PASEO_SANDBOX_KEEP_FAILED: "1",
      TECK_PASEO_KEEP_FAILED: "0",
    }),
    false,
  );
  assert.equal(
    shouldRemoveFailedSandbox(true, {
      PASEO_SANDBOX_KEEP_FAILED: "",
      TECK_PASEO_KEEP_FAILED: "1",
    }),
    false,
  );
});

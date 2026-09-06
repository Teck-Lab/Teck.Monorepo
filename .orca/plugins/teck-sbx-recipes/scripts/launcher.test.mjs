import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import test from "node:test";

const pluginRoot = resolve(import.meta.dirname, "..");
const pluginKey = "teck-lab.teck-sbx-recipes";
const validHash = "a".repeat(64);

function fixture(t, { hash = validHash, publisher = "teck-lab", id = "teck-sbx-recipes" } = {}) {
  const home = mkdtempSync(join(tmpdir(), "teck-plugin-launcher-"));
  t.after(() => rmSync(home, { recursive: true, force: true }));
  const appData = join(home, "AppData", "Roaming");
  const plugins = join(appData, "Orca", "plugins");
  const installed = join(plugins, pluginKey, hash, "scripts");
  mkdirSync(installed, { recursive: true });
  writeFileSync(
    join(plugins, "plugins.lock.json"),
    JSON.stringify({ version: 1, plugins: { [pluginKey]: { contentHash: hash } } }),
  );
  writeFileSync(
    join(plugins, pluginKey, hash, "orca-plugin.json"),
    JSON.stringify({ publisher, id }),
  );
  writeFileSync(
    join(installed, "lifecycle.mjs"),
    "let data='';process.stdin.setEncoding('utf8');process.stdin.on('data',v=>data+=v);process.stdin.on('end',()=>process.stdout.write(`${process.argv[2]}:${process.env.ORCA_REPO_PATH}:${data}`));",
  );
  return appData;
}
function runRecipe(appData, action = "create") {
  const recipe = JSON.parse(
    readFileSync(join(pluginRoot, "recipes", "teck-sbx-project-sandbox.json"), "utf8"),
  );
  return {
    recipe,
    result: spawnSync("cmd.exe", ["/d", "/s", "/c", recipe[action]], {
      encoding: "utf8",
      input: '{"payload":true}',
      env: { ...process.env, APPDATA: appData, ORCA_REPO_PATH: "C:\\unrelated\\repo" },
    }),
  };
}
test("generated commands launch the validated installed plugin copy with stdin", (t) => {
  const appData = fixture(t);
  for (const action of ["create", "suspend", "resume", "destroy"]) {
    const { recipe, result } = runRecipe(appData, action);
    assert.equal(result.status, 0, result.stderr);
    assert.ok(recipe[action].length < 7000);
    assert.equal(result.stdout.trimEnd(), `${action}:C:\\unrelated\\repo:{"payload":true}`);
  }
});

test("generated command rejects an invalid cache hash", (t) => {
  const { result } = runRecipe(fixture(t, { hash: ".." }));
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /plugin hash is invalid/);
});

test("generated command rejects a mismatched plugin identity", (t) => {
  const { result } = runRecipe(fixture(t, { publisher: "attacker" }));
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /plugin manifest is invalid/);
});

test("generated command preserves lifecycle failure exit codes", (t) => {
  const appData = fixture(t);
  const plugins = join(appData, "Orca", "plugins");
  writeFileSync(
    join(plugins, pluginKey, validHash, "scripts", "lifecycle.mjs"),
    "process.exitCode=23;",
  );
  const { result } = runRecipe(appData, "destroy");
  assert.equal(result.status, 23);
});

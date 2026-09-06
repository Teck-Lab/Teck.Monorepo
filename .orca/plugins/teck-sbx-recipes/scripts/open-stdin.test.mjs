import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import test from "node:test";

const pluginRoot = resolve(import.meta.dirname, "..");

test("create launcher does not wait for stdin EOF", async (t) => {
  const home = mkdtempSync(join(tmpdir(), "teck-open-stdin-"));
  t.after(() => rmSync(home, { recursive: true, force: true }));
  const appData = join(home, "AppData", "Roaming");
  const plugins = join(appData, "Orca", "plugins");
  const pluginKey = "teck-lab.teck-sbx-recipes";
  const hash = "b".repeat(64);
  const installed = join(plugins, pluginKey, hash, "scripts");
  mkdirSync(installed, { recursive: true });
  writeFileSync(
    join(plugins, "plugins.lock.json"),
    JSON.stringify({ version: 1, plugins: { [pluginKey]: { contentHash: hash } } }),
  );
  writeFileSync(
    join(plugins, pluginKey, hash, "orca-plugin.json"),
    JSON.stringify({ publisher: "teck-lab", id: "teck-sbx-recipes" }),
  );
  writeFileSync(join(installed, "lifecycle.mjs"), "process.stdout.write('created');");
  const recipe = JSON.parse(
    readFileSync(join(pluginRoot, "recipes", "teck-sbx-project-sandbox.json"), "utf8"),
  );
  const child = spawn("cmd.exe", ["/d", "/s", "/c", recipe.create], {
    env: { ...process.env, APPDATA: appData },
    stdio: ["pipe", "pipe", "pipe"],
  });
  let stdout = "";
  child.stdout.setEncoding("utf8").on("data", (value) => {
    stdout += value;
  });
  const exit = await Promise.race([
    new Promise((resolveExit) => child.on("exit", (code) => resolveExit({ code }))),
    new Promise((resolveTimeout) => setTimeout(() => resolveTimeout({ timeout: true }), 3000)),
  ]);
  if (exit.timeout) child.kill();
  assert.deepEqual(exit, { code: 0 });
  assert.equal(stdout, "created");
});

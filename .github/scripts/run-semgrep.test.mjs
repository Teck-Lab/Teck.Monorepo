import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

function runScan(paths, product) {
  const workflow = JSON.parse(execFileSync("bun", ["-e", 'console.log(JSON.stringify(Bun.YAML.parse(await Bun.file(".github/workflows/security-scans.yml").text()).jobs.semgrep.steps.find(s=>s.name==="Run Semgrep (SARIF)")))'], { encoding: "utf8" }));
  const dir = mkdtempSync(join(tmpdir(), "semgrep-args-"));
  const argsFile = join(dir, "args.json");
  const semgrep = join(dir, "semgrep");
  writeFileSync(semgrep, '#!/usr/bin/env node\nrequire("node:fs").writeFileSync(process.env.ARGS_FILE, JSON.stringify(process.argv.slice(2)));\n', { mode: 0o755 });
  const expressions = {
    "matrix.product": product,
    "matrix.scanPaths": JSON.stringify(paths),
    "toJSON(matrix.scanPaths)": JSON.stringify(paths),
    "join(matrix.scanPaths, ' ')": paths.join(" "),
  };
  const expand = (value) => value.replace(/\$\{\{\s*(.*?)\s*\}\}/g, (_, expression) => {
    assert.ok(Object.hasOwn(expressions, expression), `unexpected expression ${expression}`);
    return expressions[expression];
  });
  const environment = Object.fromEntries(Object.entries(workflow.env ?? {}).map(([key, value]) => [key, expand(value)]));
  try {
    execFileSync("bash", ["-c", expand(workflow.run)], {
      env: { ...process.env, PATH: `${dir}:${process.env.PATH}`, ARGS_FILE: argsFile, ...environment },
      stdio: "pipe",
    });
    return JSON.parse(readFileSync(argsFile, "utf8"));
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

test("Semgrep scans both gateway hosts and edge library under the transitional category", () => {
  const args = runScan([
    "src/services/gateway/web-admin",
    "src/services/gateway/web-customer",
    "src/services/gateway/shared",
  ], "gateway-public");
  assert.deepEqual(args.slice(-4), [
    "src/services/gateway/web-admin",
    "src/services/gateway/web-customer",
    "src/services/gateway/shared",
    "src/shared",
  ]);
  assert.equal(args[args.indexOf("--output") + 1], "semgrep-gateway-public.sarif");
});

test("matrix data with shell syntax is passed as one literal scan argument", () => {
  const path = "src/services/gateway/web-admin; echo injected";
  const args = runScan([path], "gateway-web-admin");
  assert.deepEqual(args.slice(-2), [path, "src/shared"]);
});

test("matrix product cannot execute shell syntax in SARIF output name", () => {
  const product = "gateway-web-admin; echo injected";
  const args = runScan(["src/services/gateway/web-admin"], product);
  assert.equal(args[args.indexOf("--output") + 1], `semgrep-${product}.sarif`);
  assert.deepEqual(args.slice(-2), ["src/services/gateway/web-admin", "src/shared"]);
});

test("matrix paths with newlines remain distinct scanner arguments", () => {
  const path = "src/services/gateway/web-admin\nnext-line";
  const args = runScan([path], "gateway-web-admin");
  assert.deepEqual(args.slice(-2), [path, "src/shared"]);
});

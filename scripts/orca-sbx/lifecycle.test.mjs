import assert from "node:assert/strict";
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import {
  configuredApiKey,
  customSecretTargetHosts,
  recipeResult,
  redactSensitive,
  remoteWindowsPath,
  sandboxName,
  shellQuote,
  supportsSbxVersion,
  wakeCheckCommand,
} from "./lifecycle.mjs";

function withFreshHome(t) {
  const home = mkdtempSync(join(tmpdir(), "orca-sbx-home-"));
  const keys = ["OMNIROUTE_API_KEY", "ORCA_OMNIROUTE_ENV_FILE"];
  const previous = new Map(keys.map((key) => [key, process.env[key]]));
  for (const key of keys) delete process.env[key];
  t.after(() => {
    for (const [key, value] of previous) {
      if (value === undefined) delete process.env[key];
      else process.env[key] = value;
    }
    rmSync(home, { recursive: true, force: true });
  });
  return home;
}

function writeHomeCredential(home, value) {
  const dir = join(home, ".config", "teck");
  mkdirSync(dir, { recursive: true });
  writeFileSync(join(dir, "omniroute.env"), `OMNIROUTE_API_KEY=${value}\n`);
}

test("sandbox names are deterministic, safe, and capped", () => {
  const name = sandboxName("local.docker sandbox", "ABC_123");
  assert.equal(name, "orca-local-docker-sandbox-abc-123");
  assert.match(name, /^orca-[a-z0-9-]+$/);
  assert.ok(sandboxName("x".repeat(100), "instance").length <= 63);
});

test("Windows workspace paths map to Docker Sandbox paths", () => {
  assert.equal(remoteWindowsPath("C:\\Users\\jacob\\Repo"), "/c/Users/jacob/Repo");
  assert.throws(() => remoteWindowsPath("/home/jacob/repo"), /absolute Windows drive path/);
});

test("remote paths are shell quoted without interpolation", () => {
  assert.equal(shellQuote("/c/Users/Jacob's Repo"), "'/c/Users/Jacob'\"'\"'s Repo'");
});
test("sensitive values are removed from lifecycle errors", () => {
  const secret = "omniroute-real-key";
  assert.equal(
    redactSensitive(`sbx --value ${secret}: rejected ${secret}`, [secret]),
    "sbx --value <redacted>: rejected <redacted>",
  );
});

test("Docker Sandbox version gate requires the supported 0.39 line", () => {
  assert.equal(supportsSbxVersion("sbx version: v0.39.0 def8cb"), true);
  assert.equal(supportsSbxVersion("sbx version: v0.40.1"), true);
  assert.equal(supportsSbxVersion("sbx version: v1.0.0"), true);
  assert.equal(supportsSbxVersion("sbx version: v0.38.9"), false);
  assert.equal(supportsSbxVersion("unknown"), false);
});


test("SSH result preserves Orca default checkout ownership", () => {
  const result = recipeResult("orca-example-1", "/c/repo");
  assert.equal(result.schemaVersion, 1);
  assert.equal(result.connection.type, "ssh");
  assert.equal(result.connection.projectRoot, "/c/repo");
  assert.equal(result.connection.target.host, "orca-example-1.sbx");
  assert.equal(result.checkoutMode, undefined);
  assert.equal(result.pairingCode, undefined);
});

test("custom secret is proxy-injected only for the public OmniRoute host", () => {
  assert.deepEqual(customSecretTargetHosts(), ["omniroute.tecklab.dk"]);
});

test("wake check reaches public OmniRoute through the proxy sentinel", () => {
  const command = wakeCheckCommand();
  assert.ok(command.includes("https://omniroute.tecklab.dk/v1/models"));
  assert.ok(command.includes("Authorization: Bearer proxy-managed"));
  assert.ok(command.includes("test -x /home/agent/.local/bin/orca-runtime-check"));
  assert.ok(!command.includes("host.docker.internal"));
  assert.ok(!command.includes("localhost"));
  assert.ok(!command.includes("20128"));
});

test("non-empty OMNIROUTE_API_KEY wins over every env file", (t) => {
  const home = withFreshHome(t);
  writeHomeCredential(home, "default-key");
  const explicitFile = join(home, "explicit.env");
  writeFileSync(explicitFile, "OMNIROUTE_API_KEY=explicit-key\n");
  process.env.ORCA_OMNIROUTE_ENV_FILE = explicitFile;
  process.env.OMNIROUTE_API_KEY = "env-key";
  assert.equal(configuredApiKey({ homeDir: home }), "env-key");
});

test("explicit ORCA_OMNIROUTE_ENV_FILE wins over the home default", (t) => {
  const home = withFreshHome(t);
  writeHomeCredential(home, "default-key");
  const explicitFile = join(home, "explicit.env");
  writeFileSync(explicitFile, "OMNIROUTE_API_KEY=explicit-key\n");
  process.env.ORCA_OMNIROUTE_ENV_FILE = explicitFile;
  assert.equal(configuredApiKey({ homeDir: home }), "explicit-key");
});

test("empty or whitespace-only overrides fall through to the next source", (t) => {
  const home = withFreshHome(t);
  writeHomeCredential(home, "default-key");
  process.env.OMNIROUTE_API_KEY = "   ";
  process.env.ORCA_OMNIROUTE_ENV_FILE = "";
  assert.equal(configuredApiKey({ homeDir: home }), "default-key");
});

test("host home default is <home>/.config/teck/omniroute.env when nothing else is set", (t) => {
  const home = withFreshHome(t);
  writeHomeCredential(home, '"home quoted key"');
  assert.equal(configuredApiKey({ homeDir: home }), "home quoted key");
});

test("a sibling Teck.Paseo/.env is never consulted as a credential source", (t) => {
  const home = withFreshHome(t);
  const repoRoot = join(home, "omniroute-public-endpoint");
  const paseoDir = join(home, "Teck.Paseo");
  mkdirSync(repoRoot, { recursive: true });
  mkdirSync(paseoDir);
  writeFileSync(join(paseoDir, ".env"), "OMNIROUTE_API_KEY=sibling-project-key\n");
  assert.throws(
    () => configuredApiKey({ homeDir: home }),
    /OmniRoute key not found\. Set OMNIROUTE_API_KEY, point ORCA_OMNIROUTE_ENV_FILE at a host-only env file, or store the key in .*\.config[\\/]teck[\\/]omniroute\.env/,
  );
  writeHomeCredential(home, "home-key");
  assert.equal(configuredApiKey({ homeDir: home }), "home-key");
});

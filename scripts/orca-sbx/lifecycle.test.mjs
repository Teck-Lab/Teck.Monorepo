import assert from "node:assert/strict";
import test from "node:test";

import {
  customSecretTargetHosts,
  recipeResult,
  redactSensitive,
  remoteWindowsPath,
  sandboxName,
  shellQuote,
  supportsSbxVersion,
  wakeCheckCommand,
} from "./lifecycle.mjs";

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

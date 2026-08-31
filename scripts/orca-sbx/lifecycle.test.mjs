import assert from "node:assert/strict";
import test from "node:test";

import { recipeResult, remoteWindowsPath, sandboxName, shellQuote } from "./lifecycle.mjs";

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

test("SSH result preserves Orca default checkout ownership", () => {
  const result = recipeResult("orca-example-1", "/c/repo");
  assert.equal(result.schemaVersion, 1);
  assert.equal(result.connection.type, "ssh");
  assert.equal(result.connection.projectRoot, "/c/repo");
  assert.equal(result.connection.target.host, "orca-example-1.sbx");
  assert.equal(result.checkoutMode, undefined);
  assert.equal(result.pairingCode, undefined);
});

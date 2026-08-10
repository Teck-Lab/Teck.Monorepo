import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const source = await readFile(new URL("./github-app-publish", import.meta.url), "utf8");

test("creates every promoted commit before one final branch advance", () => {
  assert.match(source, /const isFinalCommit = index === plan\.length - 1/);
  assert.match(source, /"--expected-head", remoteHead/);
  assert.match(source, /"--advance", String\(isFinalCommit\)/);
});

test("uses the current upstream when publishing a new remote branch", () => {
  assert.match(source, /fetchedBranch\.status === 0/);
  assert.match(source, /"--verify", "@\{upstream\}"/);
  assert.match(source, /"--set-upstream-to"/);
});

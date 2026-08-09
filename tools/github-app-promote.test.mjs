import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const source = await readFile(new URL("./github-app-promote", import.meta.url), "utf8");

test("uses GitHub's distinct read and update reference endpoints", () => {
  assert.match(source, /readRefPath = `\/git\/ref\/heads\/\$\{encodedBranch\}`/);
  assert.match(source, /updateRefPath = `\/git\/refs\/heads\/\$\{encodedBranch\}`/);
  assert.match(source, /request\(readRefPath, \{\}, \[404\]\)/);
  assert.match(source, /request\(updateRefPath, \{/);
});

test("advances the branch only when promotion requests it", () => {
  assert.match(source, /expectedHead = options\.get\("--expected-head"\) \?\? parent/);
  assert.match(source, /remoteRef && advance/);
  assert.match(source, /!remoteRef && advance/);
  assert.match(source, /request\(`\/git\/commits\/\$\{commit\.sha\}`\)/);
});

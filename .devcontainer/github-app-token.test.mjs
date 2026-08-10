import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const source = await readFile(new URL("./github-app-token.sh", import.meta.url), "utf8");

test("write tokens can publish workflow changes", () => {
  assert.match(source, /write\) permissions='\{"contents":"write","workflows":"write"\}'/);
});

import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import test from "node:test";

const services = JSON.parse(execFileSync("bash", [".github/scripts/discover-services.sh"], { encoding: "utf8" }));

test("operations services expand to the fixed operations release group", () => {
  const operations = services.filter((service) => service.group === "operations");
  assert.ok(operations.length > 0);
  assert.deepEqual([...new Set(operations.map((service) => service.releaseGroup))], ["operations"]);
});

test("gateway public keeps its distinct product and release group", () => {
  const gateway = services.find((service) => service.product === "gateway-public");
  assert.deepEqual(gateway?.releaseGroup, "gateway-public");
});

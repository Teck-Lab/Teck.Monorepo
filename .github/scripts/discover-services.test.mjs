import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import test from "node:test";

const services = JSON.parse(
  execFileSync("bash", [".github/scripts/discover-services.sh"], { encoding: "utf8" }),
);

test("operations services expand to the fixed operations service group", () => {
  const operations = services.filter((service) => service.group === "operations");
  assert.ok(operations.length > 0);
  assert.deepEqual([...new Set(operations.map((service) => service.releaseGroup))], ["operations"]);
});

test("both web gateways deploy independently; shared edge is only a library", () => {
  const gateways = services.filter((service) => service.group === "gateway");
  assert.deepEqual(
    gateways.map(({ service, product, nxProject, releaseGroup }) => ({ service, product, nxProject, releaseGroup })),
    [
      { service: "web-admin", product: "gateway-web-admin", nxProject: "Gateway.WebAdmin", releaseGroup: "gateway-web-admin" },
      { service: "web-customer", product: "gateway-web-customer", nxProject: "Gateway.WebCustomer", releaseGroup: "gateway-web-customer" },
    ],
  );
});

test("retiring gateway scan retains the base branch category while covering both replacements", () => {
  const matrix = JSON.parse(
    execFileSync("bash", [".github/scripts/discover-services.sh", "--semgrep-matrix"], { encoding: "utf8" }),
  );
  const legacy = matrix.find(({ product }) => product === "gateway-public");
  assert.deepEqual(legacy.scanPaths, [
    "src/services/gateway/web-admin",
    "src/services/gateway/web-customer",
    "src/services/gateway/shared",
  ]);
  assert.deepEqual(
    matrix.filter(({ product }) => product.startsWith("gateway-") && product !== "gateway-public")
      .map(({ product }) => product),
    ["gateway-web-admin", "gateway-web-customer"],
  );
  assert.equal(matrix.length, services.length + 1);
});

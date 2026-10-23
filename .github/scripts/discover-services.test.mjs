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

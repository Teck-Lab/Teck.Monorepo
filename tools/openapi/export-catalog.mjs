import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
const source =
  process.env.CATALOG_OPENAPI_URL ?? "http://127.0.0.1:5077/openapi/v1-public/openapi.json";
const destination = resolve(
  root,
  process.env.CATALOG_OPENAPI_OUTPUT ?? "specs/catalog/products.openapi.json",
);
const response = await fetch(source);
if (!response.ok) {
  throw new Error(
    `Catalog OpenAPI export failed: ${response.status} ${response.statusText} from ${source}`,
  );
}
const check = process.argv.includes("--check");
const document = await response.json();
const allowedOperations = new Map([
  ["/products", { method: "post", resource: "catalog", scope: "manage" }],
  ["/products/{productId}", { method: "get", resource: "catalog", scope: "product-read" }],
]);
for (const [path, pathItem] of Object.entries(document.paths ?? {})) {
  const expected = allowedOperations.get(path);
  for (const method of Object.keys(pathItem)) {
    if (!expected || method !== expected.method) delete pathItem[method];
  }
  if (Object.keys(pathItem).length === 0) delete document.paths[path];
}
for (const [path, expected] of allowedOperations) {
  const operation = document.paths?.[path]?.[expected.method];
  const permission = operation?.["x-protected-resource"];
  if (
    !operation ||
    permission?.resource !== expected.resource ||
    permission?.scope !== expected.scope
  ) {
    throw new Error(
      `Catalog OpenAPI document must contain ${expected.method.toUpperCase()} ${path} with ${expected.resource}:${expected.scope}: ${source}`,
    );
  }
}
const reachableSchemas = new Set();
const visitReferences = (value) => {
  if (Array.isArray(value)) {
    for (const item of value) visitReferences(item);
  } else if (value && typeof value === "object") {
    if (typeof value.$ref === "string" && value.$ref.startsWith("#/components/schemas/")) {
      const name = value.$ref.slice("#/components/schemas/".length);
      if (!reachableSchemas.has(name)) {
        reachableSchemas.add(name);
        visitReferences(document.components?.schemas?.[name]);
      }
    }
    for (const child of Object.values(value)) visitReferences(child);
  }
};
for (const pathItem of Object.values(document.paths)) {
  for (const operation of Object.values(pathItem)) visitReferences(operation);
}
for (const name of Object.keys(document.components?.schemas ?? {})) {
  if (!reachableSchemas.has(name)) delete document.components.schemas[name];
}
if (document.components && Object.keys(document.components.schemas ?? {}).length === 0) {
  delete document.components.schemas;
}
if (document.components && Object.keys(document.components).length === 0)
  delete document.components;
// The configured client base URL is authoritative; a runtime host/port is not part of the contract.
delete document.servers;
for (const [name, scheme] of Object.entries(document.components?.securitySchemes ?? {})) {
  if (scheme.flows) {
    for (const flow of Object.values(scheme.flows)) {
      delete flow.authorizationUrl;
      delete flow.tokenUrl;
      delete flow.refreshUrl;
    }
  }
  if (scheme.openIdConnectUrl) delete scheme.openIdConnectUrl;
  if (
    scheme.flows &&
    Object.values(scheme.flows).every(
      (flow) => !flow.authorizationUrl && !flow.tokenUrl && !flow.refreshUrl,
    )
  ) {
    delete scheme.flows;
  }
  if (Object.keys(scheme).length === 0) delete document.components.securitySchemes[name];
}
for (const operation of Object.values(document.paths).flatMap((pathItem) =>
  Object.values(pathItem),
)) {
  operation.security = (operation.security ?? []).filter((requirement) =>
    Object.keys(requirement).every((name) => document.components?.securitySchemes?.[name]),
  );
  if (operation.security.length === 0) delete operation.security;
}
if (document.components && Object.keys(document.components.securitySchemes ?? {}).length === 0) {
  delete document.components.securitySchemes;
}
if (document.components && Object.keys(document.components).length === 0)
  delete document.components;
const serialized = JSON.stringify(document, null, 2);
const output = `${serialized.replace(/\[\n(\s+)"([^"\n]+)"\n\s+\]/g, '["$2"]')}\n`;
if (check) {
  const checkedIn = await readFile(destination, "utf8");
  if (checkedIn !== output) {
    throw new Error(
      "Catalog OpenAPI contract drift detected; run `nx run openapi-tools:export:catalog`.",
    );
  }
} else {
  await mkdir(dirname(destination), { recursive: true });
  await writeFile(destination, output);
}

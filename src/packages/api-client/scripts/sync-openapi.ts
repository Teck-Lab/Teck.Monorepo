import { existsSync } from "node:fs";
import { mkdir } from "node:fs/promises";
import { resolve } from "node:path";

const root = resolve(import.meta.dir, "..");
const output = resolve(root, "openapi/teck-platform.openapi.json");
const check = process.argv.includes("--check");
const services = [
  ["catalog", process.env.CATALOG_OPENAPI_URL ?? "http://127.0.0.1:5101/openapi/v1/openapi.json"],
  ["pricing", process.env.PRICING_OPENAPI_URL ?? "http://127.0.0.1:5102/openapi/v1/openapi.json"],
] as const;

type Document = {
  openapi: string;
  paths: Record<string, unknown>;
  components: { schemas: Record<string, unknown>; securitySchemes?: Record<string, unknown> };
};

const documents = await Promise.all(
  services.map(async ([name, url]) => {
    const response = await fetch(url);
    if (!response.ok) throw new Error(`${name} OpenAPI returned HTTP ${response.status}`);
    const document = (await response.json()) as Document;
    if (!document.openapi?.startsWith("3.") || !document.paths || !document.components?.schemas)
      throw new Error(`${name} did not provide an OpenAPI 3 document`);
    return [name, document] as const;
  }),
);

const paths: Record<string, unknown> = {};
const schemas: Record<string, unknown> = {};
const securitySchemes: Record<string, unknown> = {};
for (const [name, document] of documents) {
  for (const [path, value] of Object.entries(document.paths)) {
    if (path in paths) throw new Error(`Conflicting service path ${path} (${name})`);
    paths[path] = value;
  }
  for (const [schema, value] of Object.entries(document.components.schemas)) {
    if (schema in schemas && JSON.stringify(schemas[schema]) !== JSON.stringify(value))
      throw new Error(`Conflicting schema ${schema} (${name})`);
    schemas[schema] = value;
  }
  for (const [scheme, value] of Object.entries(document.components.securitySchemes ?? {})) {
    if (
      scheme in securitySchemes &&
      JSON.stringify(securitySchemes[scheme]) !== JSON.stringify(value)
    )
      throw new Error(`Conflicting security scheme ${scheme} (${name})`);
    securitySchemes[scheme] = value;
  }
}

// The public YARP gateway exposes the service paths at its root; internal
// server URLs are intentionally not carried into the client contract.
const merged = {
  openapi: "3.0.0",
  info: { title: "Teck Catalog and Pricing gateway API", version: "1.0.0" },
  servers: [{ url: "/" }],
  paths: Object.fromEntries(Object.entries(paths).sort(([a], [b]) => a.localeCompare(b))),
  components: {
    schemas: Object.fromEntries(Object.entries(schemas).sort(([a], [b]) => a.localeCompare(b))),
    securitySchemes: Object.fromEntries(
      Object.entries(securitySchemes).sort(([a], [b]) => a.localeCompare(b)),
    ),
  },
};
const serialized = `${JSON.stringify(merged, null, 2)}\n`;
if (check) {
  if (!existsSync(output) || (await Bun.file(output).text()) !== serialized)
    throw new Error(
      "Gateway OpenAPI snapshot differs from live Catalog/Pricing; run bun run sync-openapi and regenerate the client",
    );
} else {
  await mkdir(resolve(root, "openapi"), { recursive: true });
  await Bun.write(output, serialized);
}

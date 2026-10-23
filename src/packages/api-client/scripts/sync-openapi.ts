import { readFile } from "node:fs/promises";
import { resolve } from "node:path";

const root = resolve(import.meta.dir, "..");
const output = resolve(root, "openapi/teck-platform.openapi.json");
const source = resolve(
  root,
  "../../services/gateway/shared/Contracts/gateway-admin-v1.openapi.json",
);
const check = process.argv.includes("--check");
type Document = {
  openapi: string;
  info: { title: string; version: string };
  servers: unknown[];
  paths: Record<string, unknown>;
  components: { schemas: Record<string, unknown>; securitySchemes?: Record<string, unknown> };
};

const document = JSON.parse(await readFile(source, "utf8")) as Document;
if (!document.openapi?.startsWith("3.") || !document.paths || !document.components?.schemas)
  throw new Error("Gateway OpenAPI contract is not a valid OpenAPI 3 document");

const merged: Document = {
  ...document,
  paths: Object.fromEntries(Object.entries(document.paths).sort(([a], [b]) => a.localeCompare(b))),
  components: {
    ...document.components,
    schemas: Object.fromEntries(
      Object.entries(document.components.schemas).sort(([a], [b]) => a.localeCompare(b)),
    ),
    securitySchemes: Object.fromEntries(
      Object.entries(document.components.securitySchemes ?? {}).sort(([a], [b]) =>
        a.localeCompare(b),
      ),
    ),
  },
};
const serialized = `${JSON.stringify(merged, null, 2)}\n`;
if (check) {
  if ((await Bun.file(output).text()) !== serialized)
    throw new Error(
      "Gateway OpenAPI snapshot differs from the checked gateway contract; run bun run sync-openapi and regenerate the client",
    );
} else {
  await Bun.write(output, serialized);
}

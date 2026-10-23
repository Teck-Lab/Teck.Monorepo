import { readFile, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";

const directory = dirname(fileURLToPath(import.meta.url));
const source = JSON.parse(await readFile(resolve(directory, "gateway-source.openapi.json"), "utf8"));
const manifest = JSON.parse(await readFile(resolve(directory, "gateway-v1.operations.json"), "utf8"));
const check = process.argv.includes("--check");

for (const audience of ["customer", "admin"] as const) {
  const paths: Record<string, Record<string, unknown>> = {};
  for (const { path, method, sourcePath } of manifest[audience]) {
    const operation = source.paths[sourcePath]?.[method];
    if (!operation) throw new Error(`Missing source operation ${method.toUpperCase()} ${sourcePath}`);
    if (paths[path]?.[method]) throw new Error(`Duplicate gateway operation ${method.toUpperCase()} ${path}`);
    paths[path] ??= {};
    paths[path][method] = operation;
  }
  const schemas: Record<string, unknown> = {};
  const pending = [...JSON.stringify(paths).matchAll(/#\/components\/schemas\/([^"}]+)/g)].map((match) => match[1]);
  for (const name of pending) {
    if (name in schemas) continue;
    const schema = source.components.schemas[name];
    if (!schema) throw new Error(`Missing referenced schema ${name}`);
    schemas[name] = schema;
    pending.push(...[...JSON.stringify(schema).matchAll(/#\/components\/schemas\/([^"}]+)/g)].map((match) => match[1]));
  }
  const document = {
    openapi: "3.0.0",
    info: { title: `Teck web-${audience} gateway API`, version: "1.0.0" },
    servers: [{ url: "/" }],
    paths,
    components: { schemas, securitySchemes: source.components.securitySchemes },
  };
  const output = resolve(directory, `gateway-${audience}-v1.openapi.json`);
  const rendered = `${JSON.stringify(document, null, 2)}\n`;
  if (check) {
    const current = await readFile(output, "utf8");
    if (current !== rendered) throw new Error(`Gateway OpenAPI out of sync: ${output}`);
  } else {
    await writeFile(output, rendered);
  }
}

import { readFile, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
const contract = resolve(root, "specs/catalog/products.openapi.json");
const document = JSON.parse(await readFile(contract, "utf8"));
await writeFile(contract, `${JSON.stringify(document, null, 2)}\n`);

import { execFileSync } from "node:child_process";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const packageRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const root = resolve(packageRoot, "../../..");
const temporaryDirectory = await mkdtemp(resolve(tmpdir(), "teck-api-client-"));
const generatedFile = resolve(packageRoot, "src/generated/catalog.ts");
const temporaryFile = resolve(temporaryDirectory, "catalog.ts");
try {
  execFileSync(
    "bunx",
    [
      "openapi-typescript",
      resolve(root, "specs/catalog/products.openapi.json"),
      "-o",
      temporaryFile,
    ],
    { cwd: root, stdio: "inherit" },
  );
  const [checked, generated] = await Promise.all([
    readFile(generatedFile, "utf8"),
    readFile(temporaryFile, "utf8"),
  ]);
  if (checked !== generated) {
    throw new Error(
      "Generated Catalog client is stale; run `nx run api-client:generate` and review the result.",
    );
  }
} finally {
  await rm(temporaryDirectory, { recursive: true, force: true });
}

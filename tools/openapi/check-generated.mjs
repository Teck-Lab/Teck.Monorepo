import { execFileSync } from "node:child_process";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { resolve } from "node:path";

const root = process.cwd();
const temporaryDirectory = await mkdtemp(resolve(tmpdir(), "teck-catalog-contract-"));
const temporarySpec = resolve(temporaryDirectory, "products.openapi.json");
const temporaryClient = resolve(temporaryDirectory, "catalog.ts");
try {
  execFileSync("node", ["tools/openapi/export-catalog.mjs"], {
    cwd: root,
    env: {
      ...process.env,
      CATALOG_OPENAPI_URL:
        process.env.CATALOG_OPENAPI_URL ?? "http://127.0.0.1:5077/openapi/v1-public/openapi.json",
      CATALOG_OPENAPI_OUTPUT: temporarySpec,
    },
    stdio: "inherit",
  });
  const currentSpec = await readFile(resolve(root, "specs/catalog/products.openapi.json"), "utf8");
  const liveSpec = await readFile(temporarySpec, "utf8");
  if (currentSpec !== liveSpec) {
    throw new Error(
      "Catalog OpenAPI contract drift detected; run `nx run openapi-tools:export:catalog`.",
    );
  }
  execFileSync("bunx", ["openapi-typescript", temporarySpec, "-o", temporaryClient], {
    cwd: root,
    stdio: "inherit",
  });
  const [currentClient, liveClient] = await Promise.all([
    readFile(resolve(root, "src/packages/api-client/src/generated/catalog.ts"), "utf8"),
    readFile(temporaryClient, "utf8"),
  ]);
  if (currentClient !== liveClient) {
    throw new Error(
      "Generated Catalog TypeScript client drift detected; run `nx run api-client:generate`.",
    );
  }
} finally {
  await rm(temporaryDirectory, { recursive: true, force: true });
}

import { readdirSync, readFileSync } from "node:fs";

const nxConfig = JSON.parse(readFileSync(new URL("./nx.json", import.meta.url), "utf8"));

// Release groups are the source of truth for domain scopes.
export const scopeProjects = Object.fromEntries(
  Object.entries(nxConfig.release.groups).map(([name, group]) => [name, group.projects]),
);

// Operations contains several separately named service domains.
for (const directory of readdirSync(new URL("./src/services/operations/", import.meta.url), {
  withFileTypes: true,
})) {
  if (directory.isDirectory()) {
    scopeProjects[directory.name] = [`src/services/operations/${directory.name}/**/*`];
  }
}

for (const directory of readdirSync(new URL("./src/packages/", import.meta.url), {
  withFileTypes: true,
})) {
  if (directory.isDirectory()) {
    scopeProjects[directory.name] = [`src/packages/${directory.name}`];
  }
}
scopeProjects.mobile = ["src/apps/mobile"];
scopeProjects.shared = ["src/shared/**/*"];

// These scopes describe repository work rather than a directly released group.
const repositoryScopes = [
  "aspire",
  "build",
  "ci",
  "deps",
  "deps-dev",
  "deploy",
  "docs",
  "identity",
  "release",
  "repo",
  "security",
  "test",
];

export const commitScopes = [
  ...new Set([...Object.keys(scopeProjects), ...repositoryScopes]),
].sort();

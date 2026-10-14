import { createRequire } from "node:module";
import { scopeProjects } from "../../commit-scopes.mjs";

const require = createRequire(import.meta.url);
const { findMatchingProjects } = require("nx/src/utils/find-matching-projects");
const nxReleaseUtils = require("nx/src/command-line/release/utils/shared");

export function expandCommitScopes(commits, projectGraph) {
  return commits.map((commit) => {
    if (!commit.scope) return commit;
    const projects = new Set();
    for (const scope of commit.scope.split(",").map((value) => value.trim())) {
      const patterns = scopeProjects[scope.toLowerCase()] ?? [scope];
      const matches = findMatchingProjects(patterns, projectGraph.nodes);
      // Preserve historical scopes with no matching project as indirect changes.
      for (const match of matches.length ? matches : [scope]) projects.add(match);
    }
    return { ...commit, scope: [...projects].join(",") };
  });
}

// Nx has no public scope-alias hook. Normalize only the scopes at its shared
// version/changelog boundary; Nx still owns affectedness and SemVer calculation.
export function installReleaseScopeAliases() {
  const original = nxReleaseUtils.getCommitsRelevantToProjects;
  nxReleaseUtils.getCommitsRelevantToProjects = (projectGraph, commits, ...args) =>
    original(projectGraph, expandCommitScopes(commits, projectGraph), ...args);
}

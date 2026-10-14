import { execFileSync } from "node:child_process";
import { appendFileSync } from "node:fs";
import { releaseBranches, releaseChecksPassed } from "./platform-release.mjs";

const branch = process.env.RELEASE_BRANCH;
const sha = process.env.RELEASE_SOURCE_SHA;
const runs = JSON.parse(
  execFileSync(
    "gh",
    [
      "api",
      "--paginate",
      "--slurp",
      `repos/${process.env.GITHUB_REPOSITORY}/actions/runs?head_sha=${sha}&event=push&per_page=100`,
    ],
    { encoding: "utf8" },
  ),
).flatMap((page) => page.workflow_runs);
const ready = releaseBranches.includes(branch) && releaseChecksPassed(runs, sha, branch);
appendFileSync(process.env.GITHUB_OUTPUT, `ready=${ready}\n`);
console.log(
  ready
    ? "CI and Security Scans passed for this exact release commit."
    : "Waiting for both workflows to pass for this commit.",
);

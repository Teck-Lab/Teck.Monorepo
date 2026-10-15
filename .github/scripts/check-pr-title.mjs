import { pathToFileURL } from "node:url";
import conventional from "@commitlint/config-conventional";
import lint from "@commitlint/lint";
import config from "../../commitlint.config.mjs";

export async function lintPullRequestTitle(title, author) {
  const rules = { ...conventional.rules, ...config.rules };
  if (author === "dependabot[bot]") {
    rules["header-max-length-unless-dependabot"] = [0];
    rules["subject-case-unless-dependabot"] = [0];
    rules["scope-required-unless-dependabot"] = [0];
  }
  return lint(title, rules, {
    plugins: config.plugins,
    // PR titles only contain a header; support Conventional Commits' breaking '!'.
    parserOpts: {
      headerPattern: /^(\w+)(?:\(([^)]+)\))?!?: (.*)$/,
      headerCorrespondence: ["type", "scope", "subject"],
    },
  });
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const result = await lintPullRequestTitle(process.env.PR_TITLE ?? "", process.env.PR_AUTHOR);
  for (const error of result.errors) console.error(`${error.name}: ${error.message}`);
  if (!result.valid) process.exitCode = 1;
}

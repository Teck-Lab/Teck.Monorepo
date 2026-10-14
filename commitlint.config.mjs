import { case as ensureCase } from "@commitlint/ensure";
import { commitScopes } from "./commit-scopes.mjs";

const forbiddenSubjectCases = ["sentence-case", "start-case", "pascal-case", "upper-case"];
const dependabotSignature = "Signed-off-by: dependabot[bot] <support@github.com>";
const startsWithLetter = /^[\p{Ll}\p{Lu}\p{Lt}]/iu;

export function subjectCaseUnlessDependabot(parsed) {
  const messageBody = [parsed.body, parsed.footer].filter(Boolean).join("\n");
  if (messageBody.includes(dependabotSignature)) return [true];

  const { subject } = parsed;
  if (typeof subject !== "string" || !startsWithLetter.test(subject)) return [true];

  const forbidden = forbiddenSubjectCases.some((subjectCase) => ensureCase(subject, subjectCase));
  return [!forbidden, `subject must not be ${forbiddenSubjectCases.join(", ")}`];
}

export function scopeRequiredUnlessDependabot(parsed) {
  const messageBody = [parsed.body, parsed.footer].filter(Boolean).join("\n");
  return [Boolean(parsed.scope) || messageBody.includes(dependabotSignature), "scope is required"];
}

export default {
  extends: ["@commitlint/config-conventional"],
  plugins: [
    {
      rules: {
        "subject-case-unless-dependabot": subjectCaseUnlessDependabot,
        "scope-required-unless-dependabot": scopeRequiredUnlessDependabot,
      },
    },
  ],
  rules: {
    "scope-case": [2, "always", "lower-case"],
    "scope-enum": [2, "always", commitScopes],
    "scope-required-unless-dependabot": [2, "always"],
    "body-max-line-length": [0],
    "subject-case": [0],
    "subject-case-unless-dependabot": [2, "always"],
  },
};

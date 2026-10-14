import { case as ensureCase } from "@commitlint/ensure";
import { commitScopes } from "./commit-scopes.mjs";

const forbiddenSubjectCases = ["sentence-case", "start-case", "pascal-case", "upper-case"];
const dependabotSignature = "Signed-off-by: dependabot[bot] <support@github.com>";
const startsWithLetter = /^[\p{Ll}\p{Lu}\p{Lt}]/iu;

function hasDependabotTrailer(parsed) {
  const messageBody = [parsed.body, parsed.footer].filter(Boolean).join("\n");
  return messageBody.split(/\r?\n/).includes(dependabotSignature);
}

export function headerMaxLengthUnlessDependabot(parsed, _when, maxLength) {
  if (hasDependabotTrailer(parsed)) return [true];
  const length = parsed.header?.length ?? 0;
  return [
    length <= maxLength,
    `header must not be longer than ${maxLength} characters, current length is ${length}`,
  ];
}

export function subjectCaseUnlessDependabot(parsed) {
  if (hasDependabotTrailer(parsed)) return [true];

  const { subject } = parsed;
  if (typeof subject !== "string" || !startsWithLetter.test(subject)) return [true];

  const forbidden = forbiddenSubjectCases.some((subjectCase) => ensureCase(subject, subjectCase));
  return [!forbidden, `subject must not be ${forbiddenSubjectCases.join(", ")}`];
}

export function scopeRequiredUnlessDependabot(parsed) {
  return [Boolean(parsed.scope) || hasDependabotTrailer(parsed), "scope is required"];
}

export default {
  extends: ["@commitlint/config-conventional"],
  plugins: [
    {
      rules: {
        "header-max-length-unless-dependabot": headerMaxLengthUnlessDependabot,
        "subject-case-unless-dependabot": subjectCaseUnlessDependabot,
        "scope-required-unless-dependabot": scopeRequiredUnlessDependabot,
      },
    },
  ],
  rules: {
    "header-max-length": [0],
    "header-max-length-unless-dependabot": [2, "always", 100],
    "scope-case": [2, "always", "lower-case"],
    "scope-enum": [2, "always", commitScopes],
    "scope-required-unless-dependabot": [2, "always"],
    "body-max-line-length": [0],
    "subject-case": [0],
    "subject-case-unless-dependabot": [2, "always"],
  },
};

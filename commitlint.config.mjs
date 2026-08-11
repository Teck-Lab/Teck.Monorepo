import { case as ensureCase } from "@commitlint/ensure";

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

export default {
  extends: ["@commitlint/config-conventional"],
  plugins: [
    {
      rules: {
        "subject-case-unless-dependabot": subjectCaseUnlessDependabot,
      },
    },
  ],
  rules: {
    "subject-case": [0],
    "subject-case-unless-dependabot": [2, "always"],
  },
};

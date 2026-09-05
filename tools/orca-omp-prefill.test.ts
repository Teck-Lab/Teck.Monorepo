import { describe, expect, test } from "bun:test";

import { submitOrcaIssue } from "../.omp/extensions/orca-prefill";

function startupActions(initialEditorText = "") {
  const sent: string[] = [];
  return {
    actions: {
      getEditorText: () => initialEditorText,
      sendUserMessage: (content: string) => {
        sent.push(content);
      },
    },
    sent,
  };
}

describe("Orca OMP startup draft", () => {
  test("submits the injected linked issue URL", () => {
    const input = startupActions();
    const environment = {
      ORCA_OMP_PREFILL: "  https://github.com/Teck-Lab/Teck.Monorepo/issues/651  ",
    };

    expect(submitOrcaIssue(input.actions, environment)).toBe(true);
    expect(input.sent).toEqual(["https://github.com/Teck-Lab/Teck.Monorepo/issues/651"]);
    expect(environment.ORCA_OMP_PREFILL).toBeUndefined();
  });

  test("falls back to Orca linked workspace metadata", () => {
    const input = startupActions();

    expect(
      submitOrcaIssue(
        input.actions,
        {},
        () => "https://github.com/Teck-Lab/Teck.Monorepo/issues/651",
      ),
    ).toBe(true);
    expect(input.sent).toEqual(["https://github.com/Teck-Lab/Teck.Monorepo/issues/651"]);
  });

  test("does not submit over text entered during OMP startup", () => {
    const input = startupActions("keep my draft");
    const environment = {
      ORCA_OMP_PREFILL: "https://github.com/Teck-Lab/Teck.Monorepo/issues/651",
    };

    expect(submitOrcaIssue(input.actions, environment)).toBe(false);
    expect(input.sent).toEqual([]);
    expect(environment.ORCA_OMP_PREFILL).toBeUndefined();
  });

  test("ignores a missing or blank Orca draft", () => {
    const input = startupActions();

    expect(submitOrcaIssue(input.actions, {}, () => undefined)).toBe(false);
    expect(
      submitOrcaIssue(input.actions, { ORCA_OMP_PREFILL: "   " }, () => undefined),
    ).toBe(false);
    expect(input.sent).toEqual([]);
  });
});

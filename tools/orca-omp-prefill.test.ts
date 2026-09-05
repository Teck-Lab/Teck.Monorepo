import { describe, expect, test } from "bun:test";

import { applyOrcaPrefill } from "../.omp/extensions/orca-prefill";

function editor(initial = "") {
  let value = initial;
  return {
    ui: {
      getEditorText: () => value,
      setEditorText: (text: string) => {
        value = text;
      },
    },
    value: () => value,
  };
}

describe("Orca OMP startup draft", () => {
  test("prefills an empty composer with the linked issue URL", () => {
    const input = editor();
    const environment = {
      ORCA_OMP_PREFILL: "  https://github.com/Teck-Lab/Teck.Monorepo/issues/651  ",
    };

    expect(applyOrcaPrefill(input.ui, environment)).toBe(true);
    expect(input.value()).toBe("https://github.com/Teck-Lab/Teck.Monorepo/issues/651");
    expect(environment.ORCA_OMP_PREFILL).toBeUndefined();
  });

  test("does not overwrite text entered during OMP startup", () => {
    const input = editor("keep my draft");
    const environment = {
      ORCA_OMP_PREFILL: "https://github.com/Teck-Lab/Teck.Monorepo/issues/651",
    };

    expect(applyOrcaPrefill(input.ui, environment)).toBe(false);
    expect(input.value()).toBe("keep my draft");
    expect(environment.ORCA_OMP_PREFILL).toBeUndefined();
  });

  test("ignores a missing or blank Orca draft", () => {
    const input = editor();

    expect(applyOrcaPrefill(input.ui, {})).toBe(false);
    expect(applyOrcaPrefill(input.ui, { ORCA_OMP_PREFILL: "   " })).toBe(false);
    expect(input.value()).toBe("");
  });
});

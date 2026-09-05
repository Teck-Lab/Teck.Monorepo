import type { ExtensionAPI } from "@oh-my-pi/pi-coding-agent";

const ORCA_OMP_PREFILL = "ORCA_OMP_PREFILL";

interface EditorUi {
  getEditorText(): string;
  setEditorText(text: string): void;
}

export function applyOrcaPrefill(
  ui: EditorUi,
  environment: Record<string, string | undefined> = process.env,
): boolean {
  const draft = environment[ORCA_OMP_PREFILL]?.trim();
  if (!draft) return false;

  delete environment[ORCA_OMP_PREFILL];
  if (ui.getEditorText().length > 0) return false;

  ui.setEditorText(draft);
  return true;
}

export default function orcaPrefillExtension(pi: ExtensionAPI): void {
  pi.on("session_start", (_event, context) => {
    applyOrcaPrefill(context.ui);
  });
}

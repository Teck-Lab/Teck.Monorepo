import type { ExtensionAPI } from "@oh-my-pi/pi-coding-agent";

const ORCA_OMP_PREFILL = "ORCA_OMP_PREFILL";

interface EditorUi {
  getEditorText(): string;
  setEditorText(text: string): void;
}
type LinkedIssueDraftResolver = () => string | undefined;

const TECK_GITHUB_ISSUES = "https://github.com/Teck-Lab/Teck.Monorepo/issues";

function resolveLinkedIssueDraft(): string | undefined {
  try {
    const command = Bun.spawnSync(
      ["orca", "worktree", "show", "--worktree", "active", "--json"],
      { stderr: "pipe", stdout: "pipe" },
    );
    if (command.exitCode !== 0) return undefined;

    const payload = JSON.parse(command.stdout.toString());
    const issue = payload?.result?.worktree?.linkedIssue;
    if (!Number.isSafeInteger(issue) || issue <= 0) return undefined;

    return `${TECK_GITHUB_ISSUES}/${issue}`;
  } catch {
    return undefined;
  }
}


export function applyOrcaPrefill(
  ui: EditorUi,
  environment: Record<string, string | undefined> = process.env,
  linkedIssueDraft: LinkedIssueDraftResolver = resolveLinkedIssueDraft,
): boolean {
  const injectedDraft = environment[ORCA_OMP_PREFILL]?.trim();
  delete environment[ORCA_OMP_PREFILL];

  if (ui.getEditorText().length > 0) return false;

  const draft = injectedDraft || linkedIssueDraft()?.trim();
  if (!draft) return false;

  ui.setEditorText(draft);
  return true;
}

export default function orcaPrefillExtension(pi: ExtensionAPI): void {
  pi.on("session_start", (_event, context) => {
    applyOrcaPrefill(context.ui);
  });
}

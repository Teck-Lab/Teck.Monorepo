#!/usr/bin/env python3
"""Keep a parent Orca coordinator turn alive until it explicitly passes its gate."""

import json
import os
import subprocess
import sys
from pathlib import Path


def git(*args: str) -> str:
    return subprocess.run(
        ["git", *args],
        check=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL,
        text=True,
    ).stdout.strip()


def allow() -> None:
    sys.exit(0)


try:
    hook_input = json.load(sys.stdin)
    root = Path(git("rev-parse", "--show-toplevel")).resolve()
    common_git = Path(git("rev-parse", "--git-common-dir"))
    if not common_git.is_absolute():
        common_git = (root / common_git).resolve()

    state_path = common_git / "orca-feature" / "state.json"
    if not state_path.is_file():
        allow()

    state = json.loads(state_path.read_text(encoding="utf-8"))
    if root != Path(state["parentRoot"]).resolve():
        allow()
    if git("branch", "--show-current") != state["parentBranch"]:
        allow()

    gate_path = common_git / "orca-feature" / "coordinator-stop-gate.json"
    if gate_path.is_file():
        gate = json.loads(gate_path.read_text(encoding="utf-8"))
        head = git("rev-parse", "HEAD")
        if (
            gate.get("schemaVersion") == 1
            and gate.get("head") == head
            and gate.get("parentIssue") == state["parentIssue"]
            and gate.get("reason") in {"final-pr", "human-blocker"}
            and isinstance(gate.get("evidence"), str)
            and gate["evidence"].startswith("https://github.com/Teck-Lab/Teck.Monorepo/")
        ):
            allow()

    already_continued = bool(hook_input.get("stop_hook_active"))
    reason = (
        "The Orca parent coordinator is not allowed to stop yet. Run the "
        "authoritative `orca orchestration check --wait` loop, process and "
        "ack every delivery, reconcile GitHub and Orca, and dispatch the next "
        "eligible task. Do not return another progress-only final response. "
        "Only after the final PR is open with clean QA/CI, or a precise durable "
        "human blocker exists, run `tools/orca-feature allow-stop` with its "
        "evidence URL."
    )
    if already_continued:
        reason += " The previous continuation also attempted to stop without passing the gate."
    print(json.dumps({"decision": "block", "reason": reason}))
except (KeyError, OSError, ValueError, subprocess.CalledProcessError, json.JSONDecodeError):
    # Fail open outside a valid initialized parent checkout. The hook must not
    # trap ordinary Codex sessions because a repository or cache is damaged.
    allow()

---
name: security-review
description: Use before declaring implementation work complete, and before commit/push - runs the local security scans (Semgrep SAST, Gitleaks secrets, Trivy SCA) that mirror the CI gate, then triages the findings
---

# Security Review

Run the same scans CI runs, locally, **before** the work leaves the machine — so
findings are fixed while you still have context, not after a failed pipeline.

## When to use

- Before declaring implementation work complete.
- Before committing or pushing changes.
- Any time you've touched auth, crypto, input handling, file/network I/O, shell
  execution, deserialization, SQL, or dependency manifests.

## How to run

From the repo root:

```bash
./tools/security-scan.sh            # changed files vs base branch (fast — default)
./tools/security-scan.sh --secrets  # gitleaks only (seconds; the CI hard gate)
./tools/security-scan.sh --all      # whole repo
```

Exit code `0` = clean, `1` = findings, `2` = could not run.
Reports land in `.security/` (gitignored): `semgrep.sarif`, `gitleaks.json`.

First run pulls the scanner images (~1 GB for Semgrep) — that's a one-time cost.

## What each finding means

| Scanner | Finding | Severity in CI |
|---|---|---|
| **Gitleaks** | a secret in the code or git history | **HARD BLOCK** — fails the merge gate |
| **Semgrep** | SAST issue (`p/csharp`, `p/secrets`, `p/r2c-security-audit`) | goes to SecObserve; gate blocks on unresolved severity |
| **Trivy** | HIGH/CRITICAL dependency vuln | gated via Dependency-Track |

## Triage — do NOT just dump the output

For each finding:

1. **Read the rule and the actual code.** Semgrep has false positives; confirm the
   issue is real in this context before acting.
2. **Fix real issues at the root cause**, not by silencing the rule.
3. **A secret finding is never "just a test value."** Treat every gitleaks hit as
   real until proven otherwise — and if a real secret was committed, say so
   loudly: it must be **rotated**, because removing it from the code does not
   un-leak it from git history.
4. **If you believe it's a false positive**, say which rule, why it doesn't apply
   here, and let your human partner decide. Do not add a suppression unilaterally.

Report findings as: severity, file:line, the rule, what's actually wrong, and your
proposed fix. If the scan is clean, say so plainly — don't imply more assurance
than three scanners actually give.

## Honest limits

This reproduces the **findings**, not the **gate**. The real merge decision lives
server-side in SecObserve (triage state, thresholds) and Dependency-Track
policies. A clean local run makes CI very likely to pass — it does not guarantee
it. Say that rather than promising a green pipeline.

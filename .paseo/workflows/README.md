# Paseo Hub workflows (legacy project bundle)

This directory contains Paseo Hub workflows using the **legacy project-bundle model**. We use this model because it supports per-execution Git worktrees, which in turn run the Docker sandbox lifecycle from `paseo.json`.

Every workflow step selects the `teck-sandbox` environment defined in `.paseo/hub.yml`. That environment creates a fresh worktree branched from `origin/main`, so each execution runs inside its own Docker sandbox exactly like the current Paseo workspace model.

## Files

| Workflow | Event | Label / condition | Purpose |
|----------|-------|-------------------|---------|
| `triage-issue.yml` | `github.issue_created` | any new issue | Add area/type labels and a triage comment. |
| `implement-ready-issue.yml` | `github.issue_label_added` | `agent:implement` | Implement the issue, push a branch, and open a PR. |
| `review-pull-request.yml` | `github.pull_request_label_added` | `agent:review` | Fetch and review the PR with `gh`. |
| `respond-to-issue-comment.yml` | `github.issue_comment_created` | contains `@teck-agent` | Answer questions or route to the right workflow. |
| `respond-to-pr-comment.yml` | `github.pull_request_comment_created` | contains `@teck-agent` | Answer questions or route to the right workflow. |

## Required placeholders

Before deploying, replace these values in `.paseo/hub.yml` and every workflow:

- `connection: teck-lab-github` — already set.
- `from_users: [CaptainPowerTurtle]` — already set.
- `environments.teck-sandbox.daemon: desktop-t9ts32p` — already set.
- `environments.teck-sandbox.cwd: C:\Users\jacob\Documents\Repos\Teck.Monorepo` — already set; adjust if your daemon sees a different path.
- `agents.<name>.mode: bypassPermissions` — already set.

## How the Docker sandbox is created

`.paseo/hub.yml`:

```yaml
environments:
  teck-sandbox:
    kind: daemon
    daemon: desktop-t9ts32p
    cwd: "C:\\Users\\jacob\\Documents\\Repos\\Teck.Monorepo"
    worktree:
      mode: branch-off
      newBranch: hub-${{ paseo.execution.id }}
      base: origin/main
```

For every workflow step, Hub asks the daemon to create a worktree on a new branch. Paseo runs `paseo.json` setup:

```json
{
  "worktree": {
    "setup": ["node .paseo/sandbox/lifecycle.mjs ensure"],
    "teardown": ["node .paseo/sandbox/lifecycle.mjs destroy"]
  }
}
```

`lifecycle.mjs` creates the Docker sandbox, mounts the worktree, injects OmniRoute credentials, and syncs OMP config. The agent then runs inside that sandbox.

## Deployment

```sh
paseo hub login https://hub.paseo.sh
paseo hub deploy --project my-project --dry-run
paseo hub deploy --project my-project
```

Replace `my-project` with your Hub project slug.

## Notes

- The new self-contained trigger model (`.paseo/triggers/*.yml`) was removed because it dispatches to a fixed `cwd` and cannot guarantee per-execution Docker sandboxes.
- Hub validates every resource, expression, and daemon at activation. A failed sync keeps the previous active revision.

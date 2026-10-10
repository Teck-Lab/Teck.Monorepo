# GitHub issue relationship bridge

Use native MCP relationship tools when they are available. Otherwise dispatch
`paperclip-issue-relationships.yml` through the existing MCP Actions tool against
the default branch. The legacy filename is retained for existing callers; this
workflow no longer depends on Paperclip.

Only explicit `workflow_dispatch` requests run this bridge. Issue creation or
adding a `security` label does not trigger it. It does not create tracking issues,
edit their bodies, infer plans, dispatch agents, merge PRs or bypass rulesets.

## Inputs and direction

`operation` is `add` or `remove`. The source is `source-repository` plus
`issue-number`; it must be an issue, not a PR. Both source and related repositories
are limited to `Teck.Monorepo`, `Teck.Terraform`, and `Teck.GitOps` under `Teck-Lab`.

| `relationship` | Meaning of `add` | Related input |
| --- | --- | --- |
| `relates-to` | Source issue relates to related issue (not an ordering constraint) | `related-issue-number` |
| `blocked-by` | Source issue is blocked by related issue | `related-issue-number` |
| `blocking` | Source issue blocks related issue | `related-issue-number` |
| `sub-issue` | Related issue becomes a child of source issue | `related-issue-number` |
| `parent` | Related issue becomes the parent of source issue | `related-issue-number` |
| `pull-request` | Related PR is manually linked as a closing reference in the source issue's Development section | PR number in `related-issue-number` |
| `branch` | Create a **new** branch in `related-repository`, linked to the source issue | `branch-name`, optional `base-branch` |

Removing reverses the selected relationship, not the direction of the inputs.
An existing different parent is never silently replaced: explicitly remove the
old parent first. Branch removal **only unlinks**; it never deletes the branch.
The documented `createLinkedBranch` API cannot attach an already-existing,
unlinked branch. The bridge refuses that case; use GitHub's UI rather than
deleting/recreating or force-updating the branch. Create linked branches before
publishing commits to them. An omitted base uses the related repository's default
branch. Branch names must be short names, not `refs/heads/...`.

Manual PR links do not change branch bases, restack commits, or merge the PR.
They are native closing references, not neutral mentions: use them only when the
PR actually delivers the issue. GitHub controls automatic closure on merge.
Removal manages manual links only. If closing keywords in the PR body still
link it, the run fails with an explanation; update those keywords separately
through MCP without overwriting unrelated human text.

Existing four-input relates-to callers remain valid: omitted `relationship`
defaults to `relates-to`, and omitted `source-repository` to `Teck.Monorepo`.

Example dispatch inputs (source issue 812 waits for issue 813):

```json
{
  "operation": "add",
  "relationship": "blocked-by",
  "source-repository": "Teck.Monorepo",
  "issue-number": "812",
  "related-repository": "Teck.Monorepo",
  "related-issue-number": "813"
}
```

## Authentication and verification

The existing `TECK_AUTOMATION_APP_ID` repository variable and
`TECK_AUTOMATION_APP_PRIVATE_KEY` Actions secret mint a short-lived installation
token using `actions/create-github-app-token`. No personal PAT or sandbox signing
key is required. The App must be installed on all three allowlisted repositories.
It needs Issues write. Linked branch creation additionally requests Contents
write; branch unlinking requests Contents read; PR linking requests Pull requests
write. Issue-to-issue operations do not request those extra grants. If a grant
is missing, an administrator must update the App permissions
and approve the installation change; the bridge cannot grant itself access.

The bridge checks out its code from the repository's default branch with checkout
credential persistence disabled, runs offline tests and validates inputs before
minting a token. It refuses PRs passed as issues, self-links, parent and dependency
cycles, inaccessible/out-of-scope traversal, and incomplete pagination. Traversal
is bounded to 200 issues; lists to ten pages of 100. Exceeding a bound fails closed.

Each add/remove reads current state first and is idempotent. Successful writes
are read back from GitHub; hierarchy changes verify both parent and children.
The run summary reports the requested edge and verified state. Response bodies,
issue text and credentials are never printed. API failures are not successes.
There is no automatic retry of mutations; redispatch the same request to recover
idempotently after checking a failed or timed-out run.

Bridge runs serialize and queue up to GitHub's 100-run concurrency limit instead
of replacing pending requests. Other tools and humans can still write outside
this queue; GitHub validates the final mutation. Dispatch acceptance is **not**
relationship success: wait for the run to complete and inspect its result. Do not
start blocked work based only on a queued dispatch. Avoid exceeding the queue.

## Remaining API limits

Native security-alert-to-issue linking is **not implemented**. GitHub documents
the code-scanning Tracking UI, but no public write API was found in the current
REST code-scanning or GraphQL issue reference. A body URL or relates-to edge is
not a substitute for that relationship. Use GitHub's Tracking / Add existing
issue UI when a native alert link is needed. This bridge never fetches alert
payloads or detected secret values.

Issue types, priority/custom fields and GitHub Projects fields are not issue
relationships and are outside this bridge. Use the appropriate native MCP tool
where exposed; do not guess field or option IDs.

API references:

- [REST issue relationships](https://docs.github.com/en/rest/issues/issues)
- [REST sub-issues](https://docs.github.com/en/rest/issues/sub-issues)
- [REST issue dependencies](https://docs.github.com/en/rest/issues/issue-dependencies)
- [GraphQL issue mutations and linked branches](https://docs.github.com/en/graphql/reference/issues)
- [Code-scanning alert tracking UI](https://docs.github.com/en/code-security/how-tos/manage-security-alerts/manage-code-scanning-alerts/track-alerts-in-issues)
- [Actions concurrency queues](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency)

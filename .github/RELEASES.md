# Platform releases and PR previews

Auto owns the platform SemVer, GitHub Releases and PR feedback. Nx owns the
independently versioned release groups and dependency propagation. There are no
Version Plans, generated release PRs, or draft GitHub Releases.

| Branch | Publication | Deployment contract |
| --- | --- | --- |
| `main` | Stable platform release | Production |
| `next` | `-next.N` prerelease | Release candidate / staging |
| `beta` | `-beta.N` prerelease | Beta |
| `alpha` | `-alpha.N` prerelease | Alpha |

Opening a PR publishes nothing. Adding `preview` builds affected backend service
images from the proposed merge commit. Preview images use immutable
`pr-<number>-<merge-sha>-<run-id>-<attempt>` tags and development metadata. They
never create GitHub Releases, prereleases, release tags, version changes, or
`pre-released`/`released` labels. Removing/closing a PR is a future Argo CD
ApplicationSet lifecycle concern; this repository does not deploy preview environments.

## Release sequence

1. Feature/fix PRs may target any release branch. Typical promotion is
   `alpha -> beta -> next -> main`; ready fixes/features can go directly to `main`.
   Merge promotion PRs with a merge commit to preserve original changes and PRs.
2. CI and Security Scans run for every release branch. Release waits for the
   latest push run of both to succeed for the exact source SHA.
   Repeated triggers for a source already published as the latest stable release
   or the current channel candidate skip preparation and publication.
3. Nx resolves versions with Conventional Commits and scopes. Auto's native
   Conventional Commit plugin computes the platform bump. Standard SemVer
   increments stable versions; Auto's `determineNextVersion` increments channel
   candidates without repeatedly bumping the target stable version.
   Platform releases use major for breaking changes, minor for features, and
   patch for other Conventional Commit types. Nx patches affected components
   for fixes, dependencies (`chore`), builds, performance, refactors, style and
   reverts; documentation, test and CI changes retain existing component versions.
4. Only changed groups are built. Images embed their group version and source
   revision; temporary `<group-version>-build.<source-sha>` tags are scanned,
   signed and accompanied by SBOM/VEX results before promotion to group-version
   and channel tags. Digest equality is verified. `latest` is never used.
5. Component Git tags are `<unit>@v<version>`. Platform tags are `v<version>`.
   The two namespaces are isolated during Auto/Nx version lookup. Group tags
   do not create separate GitHub Releases.
6. Auto publishes one platform GitHub Release, including group-scoped notes and
   an initial overview and component versions in expandable section headers.
   Prerelease notes compare against the
   previous published candidate on that channel; stable notes compare against
   the previous stable platform release.
   Fixed-group entries are rendered once using Nx's public renderer extension.
   Each changed component and the platform changes use a collapsible section,
   closed by default, with one heading in its summary. Expanding a section shows
   the change categories directly, without repeating its title or Nx's version/date
   heading. The plain Markdown attachment retains one component/version heading.
   Unchanged components are omitted from the changelog;
   their versions and image digests remain in `platform-manifest.json`.
   Complete notes are also attached as `release-notes.md`; oversized bootstrap
   histories retain the overview and inline changelog sections for every
   changed component. Large sections are shortened at complete Markdown lines
   to fit the release body budget; the attachment retains every change.
   A compare link at the bottom uses the previous stable/candidate tag, or the
   initial commit for the first release.
7. Every release attaches `platform-manifest.json`: all components, including
   unchanged versions, their source revisions and exact image digests. A future
   GitOps deployment consumes this manifest after release publication, rather
   than discovering unvalidated build tags.

Auto's native `released` plugin owns comments and label transitions. The release
adapter passes only PRs not already labelled `pre-released`/`released` to the
prerelease notifier, so promotion across alpha, beta and next does not repeatedly
notify the same PR. Stable publication includes all PRs since the last stable
release, replaces `pre-released` with `released`, and updates the native release
comment. Already released PRs are skipped on a retry.

Merge current `main` into an active prerelease branch after shipping direct
changes to `main`. Publication refuses a candidate which omits the latest stable
platform release. Component tag selection is limited to the current branch and
preid, so unrelated channel versions cannot become its baseline.

## Scope policy

`commit-scopes.mjs` derives domain/group names from `nx.json` and the repository
layout. Examples: `feat(order): add checkout`, `fix(billing): retry payments`,
`fix(order,inventory): repair reservations`, and `ci(repo): update checks`.
Human/agent commits require a valid lowercase scope. Dependabot retains its
existing unscoped format; any scope it supplies must still be valid.

Commitlint enforces the policy locally before push and in PR CI, including
GitHub MCP-created commits. CI also validates squash PR titles when opened,
updated or edited. Required `CI Gate` and `Security Gate` checks must be enabled
for all four branches, without automation bypass; see `CI_GATES.md`.

Release preparation expands domain scopes to Nx project names. Nx still owns
affectedness and SemVer; members of a fixed group share the highest bump, while
indirect changes can cause patch bumps. Before 1.0.0, Nx's default lowers feature
bumps to patch and breaking changes to minor. The scope adapter uses an internal
Nx boundary and is covered by CI regression tests.
Nx packages are pinned together so this adapter is revalidated during upgrades.

## Preview handoff

The preview workflow uploads `preview-manifest-<PR>` with `kind: preview`, the
head/base/merge SHAs, the target branch's baseline platform tag and signed image
digest references. This is an image-build handoff only. No Argo CD/Kargo service,
Kubernetes manifests or environment provisioning is added here. Deployment
configuration belongs in Teck.GitOps.

Image publication covers the backend Host projects discovered by
`discover-services.sh`. Node apps/packages remain independently versioned in the
manifest; their existing CI builds are retained. A separate frontend/package
distribution mechanism can consume these component versions.

## Configuration and validation

The optional AI overview uses OmniRoute's OpenAI-compatible `/chat/completions`
API with the `teck-fast-text` combo. Store the API base URL (including `/v1`) as
the `OMNIROUTE_BASE_URL` Actions secret, and the key as `OMNIROUTE_API_KEY`.
Set `RELEASE_AI_SUMMARY=true` to enable it; `OMNIROUTE_MODEL` optionally overrides
the default combo. The separate summary job has repository read permissions,
sends only bounded release-note data and grants no tools. The overview artifact
is bound to the source SHA and platform version before publication. Disabled,
failed or invalid AI summaries fall back to a generated overview, and never
block publication. Versioning, tags and changelog entries are unaffected by AI.

- `.autorc.json` pins the base/prerelease branches and native Auto plugins.
- The release jobs use the Teck Automation App credentials already configured
  in `TECK_AUTOMATION_APP_ID` and `TECK_AUTOMATION_APP_PRIVATE_KEY`. The App needs
  Contents, Pull requests and Issues read/write for publication and notifications.
- Create the prerelease branches from current `main`, and apply the required
  branch rulesets before enabling publication. Auto does not create branches.
- `bun run test:release` verifies native Auto behavior, scope policy, channel
  versions, gates, immutable artifacts, notifications and preview-only manifests.
- `GH_TOKEN` with read access is required for `bun run release:prepare:dry`;
  Auto resolves the real PR metadata. Dry runs create no commits, tags or releases.
- `release:publish` is CI-only. Do not publish/tag/promote locally.
- If publication fails after building, rerun the failed publication job to reuse
  its immutable release plan and signed image results.

The workflow maintains no source version commits: Nx runs in dry-run mode for
planning, and resolved group versions are passed into artifact builds. Node
source manifests are not rewritten by release automation.

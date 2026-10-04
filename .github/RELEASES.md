# Release automation

Teck.Monorepo uses trunk-based development with Nx Version Plans and one
continuously updated release pull request.

1. Every releasable pull request adds an Nx Version Plan with
   `bun run release:plan`. The plan selects affected projects or release
   groups, the semantic bump, and the changelog entry.
2. After merges to `main`, Nx resolves all still-pending plans, propagates
   dependency bumps, updates supported Node package manifests, and writes
   `.nx/releases/manifest.json` plus group-scoped changelog notes.
3. `peter-evans/create-pull-request` maintains the signed `release/nx`
   pull request. Further merges to `main` update that same PR; the newest
   candidate always supersedes older candidates.
4. Release Drafter keeps one stable draft per affected release unit. The
   release PR builds deployable services on separate matrix runners and
   publishes immutable `<version>-canary.<40-character-source-sha>` images,
   moving `canary` aliases, and matching `canary/<unit>@v<version>-canary.<sha>`
   GitHub prereleases. The separate `canary/` tag namespace prevents prerelease
   tags from becoming Nx's stable-version baseline. The candidate tag identifies
   the build under test; the artifact itself embeds the target stable version and
   source revision so digest promotion does not leave canary metadata inside the
   stable image.
5. Merging `release/nx` promotes the already-tested image manifests to
   `<version>` and `latest` without rebuilding, then Release Drafter
   publishes the existing drafts as stable `<release-unit>@v<version>`
   releases.

There is no manual workflow dispatch or local promotion command. The release PR
is the approval and promotion control.

## Version ownership

- Nx owns release groups, Version Plans, semantic version calculation,
  dependency propagation, and `package.json` updates.
- Release Drafter owns draft/prerelease notes and creates the GitHub tags when
  the release PR is merged.
- The workflow-owned `.nx/releases/manifest.json` is the immutable contract
  joining the release PR, image matrix, prereleases, and stable promotion.
- .NET project files are not rewritten. `VERSION` and `SOURCE_SHA` are
  passed into MSBuild during the container build, producing `PackageVersion`
  and `AssemblyInformationalVersion` metadata.
- Next reads `TECK_RELEASE_VERSION` and `TECK_SOURCE_REVISION` during build
  and exposes them as `NEXT_PUBLIC_TECK_RELEASE_VERSION` and
  `NEXT_PUBLIC_TECK_SOURCE_REVISION`.
- Expo reads the same variables through `app.config.js` and exposes them in
  `expo.version` and `expo.extra`.

Nx includes `TECK_RELEASE_VERSION` and `TECK_SOURCE_REVISION` in build cache
inputs, so a version change cannot reuse an artifact built with stale metadata.

## Adding release units

Backend services are discovered under `src/services/<group>/<service>`. Add
the service projects to the appropriate Nx release group (or add a new group)
and add the matching `.github/release-drafter/<unit>.yml` configuration. New
Host projects are then picked up by the image matrix automatically.

Node apps and packages under `src/apps/*` and `src/packages/*` are
independently versioned by the `web` release group. Each needs a
`package.json` version and a matching Release Drafter configuration. Builds
receive their resolved version through `TECK_RELEASE_VERSION`; publishing a
package or frontend artifact can be added later without changing the version
contract.

## Useful commands

- `bun run release:plan` - interactively add a Version Plan.
- `bun run release:plan:check` - verify releasable changes have a plan.
- `bun run release:prepare:dry` - preview versions and release units.

Do not run `bun run release:prepare` outside the release workflow. It is
intended to update the managed release PR checkout.

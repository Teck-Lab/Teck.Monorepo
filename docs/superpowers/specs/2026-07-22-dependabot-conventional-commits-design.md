# Dependabot Conventional Commit Messages — Design

**Date:** 2026-07-22
**Status:** Approved
**Scope:** Make Dependabot-generated dependency-update commits pass the repository's conventional commit gate.

## Context

`wagoid/commitlint-github-action@v6` applies default conventional commit validation in `.github/workflows/ci.yml`. Dependabot's default subject, such as `Bump FluentStorage from 6.0.3 to 8.0.12`, has no type or subject and fails that check.

The repository already schedules weekly Dependabot updates for NuGet, npm, Docker, and GitHub Actions in `.github/dependabot.yml`. Commitlint remains strict; Dependabot is not exempted.

## Decision

Each Dependabot update entry uses a `commit-message` block with:

```yaml
prefix: "chore"
include: "scope"
```

Dependabot supplies the colon after `chore` and derives the scope, yielding `chore(deps): Bump …`. The npm entry also uses:

```yaml
prefix-development: "chore"
```

This yields `chore(deps-dev): Bump …` for npm development dependencies. `prefix-development` is not configured for NuGet, Docker, or GitHub Actions because Dependabot does not support it for those ecosystems.

## Implementation

Only `.github/dependabot.yml` changes. Its existing ecosystem directories and weekly schedules remain unchanged. No change is made to the commitlint workflow or its configuration.

Expected generated subjects:

```text
chore(deps): Bump FluentStorage from 6.0.3 to 8.0.12
chore(deps-dev): Bump a development npm dependency
chore(deps): Bump a Docker image
chore(deps): Bump an action
```

## Verification

1. Validate the Dependabot YAML structure.
2. Confirm each update entry contains the expected supported `commit-message` keys.
3. On the next Dependabot PR, confirm its generated commit subject passes the existing `wagoid/commitlint-github-action@v6` check without a bot exemption.

## Out of Scope

- Changing commitlint rules or adding bot-specific exclusions.
- Altering Dependabot schedules, directories, grouping, or update ecosystems.
- Rewriting existing Dependabot commits.

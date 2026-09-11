# Teck Docker Sandbox Recipes

Repository-local Windows adaptation of `mattjohnson/orca-sbx-recipes`, pinned in `UPSTREAM.json`.

The recipe creates one Docker Sandbox per `ORCA_PROJECT_ID`, clones the Teck project once onto the VM disk, exposes a private `sshd` on a deterministic loopback port, and reuses that sandbox for every Orca worktree in the project. Its base image includes OMP, Bun, and the repository-pinned .NET SDK. It adds Teck OMP configuration, the OmniRoute proxy-managed secret and research routing, the dedicated sandbox GPG key, and the Orca runtime check.

The plugin starts `sandboxd` when a lifecycle action runs. It also owns one Windows Task Scheduler keepalive per project sandbox. That hidden task starts at Windows logon, restores `sshd`, and keeps the VM running, so the daemon and shared project VM are available when Orca restores workspaces after a PC restart. Destroy removes the task when the last project worktree is gone.

## Build

```powershell
node .orca/plugins/teck-sbx-recipes/scripts/build-recipe.mjs
node --test .orca/plugins/teck-sbx-recipes/scripts/lifecycle.test.mjs
```

The JSON in `recipes/` is generated. Do not edit it directly.

## Install

Install this directory as a local plugin in Orca:

```text
<repository>/.orca/plugins/teck-sbx-recipes
```

Reinstall the local plugin after changing the manifest or generated recipe. Retry uses Orca's already-installed copy and does not refresh it. Create a brand-new workspace for lifecycle validation.

This plugin is intentionally Teck.Monorepo-specific. The source project must contain `.omp/config.yml`, `.omp/models.yml`, `.omp/RULES.md`, and `.omp/mcp.json` plus the kit dependencies represented by the vendored plugin.

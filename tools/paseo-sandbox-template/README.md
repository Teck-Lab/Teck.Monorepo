# Paseo sandbox template

This is a repository-neutral template for Teck's OMP and OmniRoute runtime. It does not assume a JavaScript, .NET, Nx, Next.js, or C# project, and `package.json` is not required. The bundled image, OmniRoute host, and `teck-*` model IDs are organization-specific defaults; replace them if the destination uses another image, gateway, or model catalog.

## Host prerequisites

- Paseo Desktop or `@getpaseo/cli`, with its daemon running.
- Standalone Docker Sandboxes CLI and daemon, with `sbx` available on the Paseo daemon process's `PATH`.
- Git and Node.js on the daemon's `PATH`.
- Access to the configured worker image and OmniRoute endpoint.
- An OmniRoute credential supplied through `OMNIROUTE_API_KEY`, the file named by `PASEO_OMNIROUTE_ENV_FILE`, or `~/.config/paseo/omniroute.env` containing `OMNIROUTE_API_KEY=<value>`.

## Copy into a repository

Run from `tools/paseo-sandbox-template` and replace the destination path:
The destination must already be an initialized Git repository; the lifecycle rejects directories without `.git` metadata.

```sh
cp -R template/. /path/to/initialized-git-repository/
```

PowerShell:

```powershell
$destination = "C:\path\to\initialized-git-repository"
Copy-Item -Recurse -Force .\template\* $destination
Copy-Item -Recurse -Force .\template\.omp $destination
Copy-Item -Recurse -Force .\template\.paseo $destination
```

Commit the copied files so every Paseo worktree receives the integration.

## Organization-specific values

Review these before use outside Teck:

- Worker image in `.paseo/sandbox/config.json` and `.paseo/sandbox/lifecycle.mjs`.
- OmniRoute host in the sandbox config, kit, and `.omp/models.yml`.
- `teck-*` model IDs in `.omp/config.yml`, `.omp/models.yml`, and the sandbox kit.
- Network allowlist in `.paseo/sandbox/kit/spec.yaml`.

## Lifecycle behavior

`paseo.json` runs:

```sh
node .paseo/sandbox/lifecycle.mjs ensure
```

when Paseo creates a worktree. `ensure` validates the Git repository and required configuration, creates or reuses one Docker Sandbox for that worktree, mounts the worktree and Git metadata, injects the OmniRoute credential, synchronizes OMP and Git configuration, and verifies the runtime.

When Paseo archives the worktree, it runs:

```sh
node .paseo/sandbox/lifecycle.mjs destroy
```

`destroy` removes the matching Docker Sandbox. Either command may be run manually from the repository root for diagnosis.
Additional diagnostic actions are available: `attach` verifies the sandbox exists and returns its runtime descriptor, while `name` returns the deterministic sandbox identity without creating it.

The template starts with empty MCP and LSP registries. Customize `.omp/RULES.md`, `.omp/mcp.json`, and `.omp/lsp.json` after copying.

## Optional overrides

- `PASEO_SANDBOX_IMAGE`
- `PASEO_SANDBOX_CPUS`
- `PASEO_SANDBOX_MEMORY`
- `PASEO_SANDBOX_LOCK_TIMEOUT_MS`
- `PASEO_SANDBOX_KEEP_FAILED`
- `PASEO_GPG_SIGNING_KEY_FILE`; otherwise the lifecycle checks `~/.config/paseo/sandbox-signing-key.asc`, then the legacy `~/.config/teck/sandbox-signing-key.asc`. Without a valid private key it leaves Git signing unconfigured.
- `PASEO_OMNIROUTE_BASE_URL`
- `PASEO_OMNIROUTE_ENV_FILE`
- `PASEO_WORKTREE_PATH` overrides the repository path used by lifecycle commands; Paseo normally supplies it.
- `PASEO_SBX_SCRIPT` and `PASEO_GIT_SCRIPT` are test-only command stubs and are read only when `NODE_ENV=test`.

Legacy `TECK_*` names and `~/.config/teck/*` credential paths remain fallback-only for existing repositories.

The template lifecycle is copied from `.paseo/sandbox/lifecycle.mjs`. Update both copies together; the focused test enforces byte equality.

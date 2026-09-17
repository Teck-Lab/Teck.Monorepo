# Paseo sandbox template

This is a repository-neutral template for Teck's OMP and OmniRoute runtime. It does not assume a JavaScript, .NET, Nx, Next.js, or C# project, and `package.json` is not required. The bundled image, OmniRoute host, and `teck-*` model IDs are organization-specific defaults; replace them if the destination uses another image, gateway, or model catalog.

## Host prerequisites

- Paseo Desktop or `@getpaseo/cli`, with its daemon running.
- Standalone Docker Sandboxes CLI and daemon, with `sbx` available on the Paseo daemon process's `PATH`.
- Git and Node.js on the daemon's `PATH`.
- Access to the configured worker image and OmniRoute endpoint.
- **One-time host setup:** copy `.paseo/omp-wrapper-bootstrap.ps1` from this template to `C:\Users\<you>\.paseo\omp-wrapper-bootstrap.ps1` and point Paseo's OMP provider at it. Paseo has no per-repo provider command, so this single host file finds the repo-local `scripts\omp-wrapper.ps1` at runtime.
- **GitHub credentials:** configure Docker Sandbox to inject a real GitHub token so `gh` works inside the sandbox (see the *GitHub CLI credentials* section below).

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

## One-time host setup

Paseo provider commands are global: every repo shares the same `agents.providers.omp.command` in `~/.paseo/config.json`. This template ships two wrapper pieces so you still only copy repo-local files once:

1. **Repo-local wrapper** (copied into every repository):
   - `scripts/omp-wrapper.ps1`
   - Finds `.paseo\\sandbox\\lifecycle.mjs` by walking up from the current directory.
   - Attaches the Docker Sandbox and starts OMP inside it.
   - No hard-coded repo paths.

2. **Host bootstrap** (placed once on your machine):
   - Copy `.paseo/omp-wrapper-bootstrap.ps1` to `C:\\Users\\<you>\\.paseo\\omp-wrapper-bootstrap.ps1`.
   - Point Paseo's OMP provider at it:

     ```json
     {
       "agents": {
         "providers": {
           "omp": {
             "enabled": true,
             "command": [
               "C:\\\\Windows\\\\System32\\\\WindowsPowerShell\\\\v1.0\\\\powershell.exe",
               "-NoLogo",
               "-NoProfile",
               "-NonInteractive",
               "-File",
               "C:\\\\Users\\\\<you>\\\\.paseo\\\\omp-wrapper-bootstrap.ps1"
             ],
             "models": [
               { "id": "omniroute/teck-orchestrator", "label": "Orchestrator", "isDefault": true }
             ]
           }
         }
       }
     }
     ```

   - The bootstrap locates `<cwd>\\scripts\\omp-wrapper.ps1` and forwards all arguments.
   - Replace `<you>` with your Windows username. Do not edit the bootstrap after copying.


## GitHub CLI credentials

Docker Sandbox can expose GitHub credentials as proxy-managed sentinels (for example, `GITHUB_TOKEN=proxy-managed`). Those sentinels are only useful for outbound HTTP requests that the host proxy intercepts and rewrites; the `gh` CLI cannot authenticate with a sentinel value.

To make `gh` work inside every sandbox, configure Docker Sandbox to inject the real token dynamically. Run once on the host for each machine where you use sandboxes:

```powershell
sbx secret set github --command 'gh auth token'
# or, if you store the token elsewhere:
# sbx secret set github --command 'cat C:\path\to\token.txt'
```

This makes `GITHUB_TOKEN` and `GH_TOKEN` inside the sandbox contain the actual token. The repo-local wrapper then writes that token into `~/.config/gh/hosts.yml` before every OMP session, so `gh` stays authenticated even after tokens rotate.

If you do not configure a real token, `gh` commands will fail, but the wrapper still initializes `~/.config/gh/config.yml` with `version: 1` to avoid the multi-account migration error that requires `dbus-launch`.

If `scripts\\omp-wrapper.ps1` is missing from a repository, the bootstrap fails with a clear message telling you to copy the `scripts` folder from this template.

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

The lifecycle requires only `.omp/config.yml`, `.omp/models.yml`, and `.omp/RULES.md`. The template also supplies empty optional MCP and LSP registries so they are ready for repository-specific tooling; `WATCHDOG.yml` is optional as well. Customize these files after copying.

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

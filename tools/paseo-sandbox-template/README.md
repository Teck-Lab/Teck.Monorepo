# Paseo sandbox template

This is a repository-neutral template for Teck's OMP and OmniRoute runtime. It does not assume a JavaScript, .NET, Nx, Next.js, or C# project, and `package.json` is not required. The bundled image, OmniRoute host, and `teck-*` model IDs are organization-specific defaults; replace them if the destination uses another image, gateway, or model catalog.

## Host prerequisites

- Paseo Desktop or `@getpaseo/cli`, with its daemon running.
- Standalone Docker Sandboxes CLI and daemon, with `sbx` available on the Paseo daemon process's `PATH`.
- Git and Node.js on the daemon's `PATH`.
- Access to the configured worker image and OmniRoute endpoint.
- An OmniRoute credential supplied through `OMNIROUTE_API_KEY`, the file named by `PASEO_OMNIROUTE_ENV_FILE`, or `~/.config/paseo/omniroute.env` containing `OMNIROUTE_API_KEY=<value>`.
- **GitHub credentials:** run `sbx secret set github --command 'gh auth token'` on the host. Docker Sandbox refreshes the token automatically and gives the agent access to `gh` CLI inside the sandbox. This is required for creating pull requests, opening issues, or interacting with GitHub APIs on your behalf.

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

Paseo provider commands are global: every repo shares the same `agents.providers.omp.command` in `~/.paseo/config.json`. The template ships a repo-local wrapper at `scripts\omp-wrapper.ps1`, but Paseo's static command array cannot point to a different path per repository. Place the host bootstrap once on your machine so each repo's wrapper is discovered at runtime.

1. **Repo-local wrapper** (copied into every repository):
   - `scripts/omp-wrapper.ps1`
   - Finds `.paseo\sandbox\lifecycle.mjs` by walking up from the current directory.
   - Attaches the Docker Sandbox and starts OMP inside it.
   - No hard-coded repo paths.

2. **Host bootstrap** (placed once on your machine):
   - Copy `.paseo/omp-wrapper-bootstrap.ps1` to `C:\Users\<you>\.paseo\omp-wrapper-bootstrap.ps1`.
   - Point Paseo's OMP provider at it:

     ```json
     {
       "agents": {
         "providers": {
           "omp": {
             "enabled": true,
             "command": [
               "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe",
               "-NoLogo",
               "-NoProfile",
               "-NonInteractive",
               "-File",
               "C:\\Users\\<you>\\.paseo\\omp-wrapper-bootstrap.ps1"
             ],
             "models": [
               { "id": "omniroute/teck-orchestrator", "label": "Orchestrator", "isDefault": true }
             ]
           }
         }
       }
     }
     ```

   - The bootstrap locates `<cwd>\scripts\omp-wrapper.ps1` and forwards all arguments.
   - Replace `<you>` with your Windows username. Do not edit the bootstrap after copying.

If `scripts\omp-wrapper.ps1` is missing from a repository, the bootstrap fails with a clear message telling you to copy the `scripts` folder from this template.

## Organization-specific values

Review these before use outside Teck:

- Worker image in `.paseo/sandbox/config.json` and `.paseo/sandbox/lifecycle.mjs`.
- OmniRoute host in the sandbox config, kit, and `.omp/models.yml`.
- `teck-*` model IDs in `.omp/config.yml`, `.omp/models.yml`, and the sandbox kit.
- Network allow-lists in `.paseo/sandbox/kit/spec.yaml`.

## Lifecycle

The lifecycle requires only `.omp/config.yml`, `.omp/models.yml`, and `.omp/RULES.md`. The template also supplies empty optional MCP and LSP registries so they are ready for repository-specific tooling; `WATCHDOG.yml` is optional as well. Customize these files after copying.

## Optional overrides

- Worker image: set `PASEO_SANDBOX_IMAGE` or `TECK_PASEO_SANDBOX_IMAGE` on the daemon host.
- CPUs / memory: set `PASEO_SANDBOX_CPUS` / `PASEO_SANDBOX_MEMORY`.
- OmniRoute base URL: set `PASEO_OMNIROUTE_BASE_URL` or `TECK_OMNIROUTE_BASE_URL`.

## Keeping template and monorepo in sync

The template lifecycle is copied from `.paseo/sandbox/lifecycle.mjs`. Update both copies together; the focused test enforces byte equality.

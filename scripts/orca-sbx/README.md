# Orca Docker Sandbox environment

This recipe runs on the Windows host and creates one sibling Docker Sandbox for
each Orca workspace. It does not run Docker Sandbox inside Docker Sandbox.
Orca connects through Docker's managed `*.sbx` SSH integration and retains its
default linked-worktree ownership because the recipe intentionally omits
`checkoutMode`.

OMP is installed in the pinned worker image. Its canonical non-secret
configuration is committed once under `.omp/` and linked into the sandbox's
user-level OMP location during provisioning. The OmniRoute key remains on the
host and is registered with `sbx secret`; it is never written to recipe state
or emitted in Orca JSON.

## Windows prerequisites

- Docker Desktop Docker Sandboxes (`sbx`) signed in
- `sbx setup ssh` completed
- Node.js 22 or newer
- access to `ghcr.io/teck-lab/paseo-worker:omp18.0.4-bun1.4.0`
- OmniRoute running on `127.0.0.1:20128`
- `OMNIROUTE_API_KEY` set, `ORCA_OMNIROUTE_ENV_FILE` pointing at a local env
  file, or a sibling `Teck.Paseo/.env` containing the key

Docker's SSH integration is represented by the `*.sbx` entry in the user's
OpenSSH config. Orca must use a transport that honors that OpenSSH config. If
the installed Orca build does not do so automatically, launch Orca with:

```powershell
$env:ORCA_SSH_FORCE_SYSTEM_TRANSPORT = '1'
orca open
```

## Validation

The recipe must be merged into the primary checkout before it appears in
Orca's **Run on** picker. Static validation is free:

```powershell
orca vm recipe doctor local-docker-sandbox `
  --repo-path C:\Users\jacob\Documents\Repos\Teck.Monorepo `
  --json
```

After explicit approval, the live doctor creates and destroys one sandbox:

```powershell
orca vm recipe doctor local-docker-sandbox `
  --repo-path C:\Users\jacob\Documents\Repos\Teck.Monorepo `
  --provision `
  --json
```

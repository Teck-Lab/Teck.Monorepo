# Orca Docker Sandbox environment

This recipe runs on the Windows host and creates one sibling Docker Sandbox for
each Orca workspace. It does not run Docker Sandbox inside Docker Sandbox.
Orca connects through Docker's managed `*.sbx` SSH integration and retains its
default linked-worktree ownership because the recipe intentionally omits
`checkoutMode`.

OMP is installed in the pinned worker image. Its canonical non-secret
configuration is committed once under `.omp/` and linked into the sandbox's
user-level OMP location during provisioning. The OmniRoute key is configured
once per machine as a global Docker Sandbox secret; it is never written to a
repository, recipe state, or Orca JSON.

## Windows prerequisites

- Docker Desktop Docker Sandboxes (`sbx`) signed in
- `sbx setup ssh` completed
- Node.js 22 or newer
- access to `ghcr.io/teck-lab/paseo-worker:omp18.0.4-bun1.4.0`
- OmniRoute running on `127.0.0.1:20128`

## One-time host credential setup

Configure the global OmniRoute binding once on each Windows machine. The
script securely prompts for the key, verifies it, and stores it in Docker
Sandboxes' host credential store:

```powershell
.\scripts\orca-sbx\setup-host.ps1
```

For automatic rotation from 1Password or AWS Secrets Manager, register a
dynamic reference instead; the real value never passes through the script:

```powershell
.\scripts\orca-sbx\setup-host.ps1 `
  -SecretRef 'op://Teck/OmniRoute/api-key'
```

The binding is global but restricted to requests sent to `localhost` or
`host.docker.internal`. New sandboxes receive the placeholder automatically.
After replacing a static key, recreate already-existing sandboxes. Dynamic
references use Docker's on-demand refresh policy.

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

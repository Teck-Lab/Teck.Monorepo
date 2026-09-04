# Orca Docker Sandbox environment

This Windows-host recipe creates one Docker Sandbox per
`ORCA_VM_INSTANCE_ID`. It uses the standalone `sbx` CLI; Docker Desktop and a
host Docker Engine are not dependencies. Orca connects through Docker's
managed `<name>.sbx` SSH `ProxyCommand` and retains schema-version-1,
linked-worktree checkout ownership because the recipe intentionally omits
`checkoutMode`.

OMP is installed in the pinned worker image. Its canonical non-secret
configuration is committed under `.omp/` and linked into the sandbox's
user-level OMP location during provisioning. The host lifecycle reads the
OmniRoute key, registers a sandbox-scoped custom secret with Docker's
host-side proxy, and redacts command failures. The sandbox receives only the
`proxy-managed` sentinel; the real key is never written to the sandbox, repo,
recipe JSON, or lifecycle logs.

## Windows prerequisites

- Windows 11 on a 64-bit Intel or AMD CPU
- Windows Hypervisor Platform enabled
- standalone Docker Sandboxes CLI 0.39.0 or newer, installed with
  `winget install -h Docker.sbx`
- `sbx login` and `sbx setup ssh` completed
- Windows OpenSSH client and Node.js 22 or newer
- access to `ghcr.io/teck-lab/paseo-worker:omp18.0.4-bun1.4.0`
- OmniRoute listening on the host at `127.0.0.1:20128`
- `OMNIROUTE_API_KEY` set, `ORCA_OMNIROUTE_ENV_FILE` pointing at a host-only
  env file, or a sibling `Teck.Paseo/.env` containing the key

Docker's managed SSH block lives in `%USERPROFILE%\.ssh\config` and routes
`*.sbx` through the local `sbx ssh proxy` command. Start Orca with the system
SSH transport so this `ProxyCommand` is authoritative:

```powershell
$env:ORCA_SSH_FORCE_SYSTEM_TRANSPORT = '1'
orca open
```

The recipe verifies the managed SSH block before provisioning and verifies
OMP, its committed configuration, the proxy sentinel, and OmniRoute before
returning the schema-version-1 SSH result.

## Validation

The recipe must be merged into the primary checkout before it appears in
Orca's **Run on** picker. Static validation is free and does not create a
sandbox:

```powershell
orca vm recipe doctor local-docker-sandbox --repo-path $PWD.Path --json
```

After Orca attaches over SSH and opens the parent OMP coordinator, its first
runtime check is:

```sh
/home/agent/.local/bin/orca-runtime-check
```

This fails unless the attached environment can reach the Orca runtime, load
the version-matched `orchestration` skill, and call the read-only
orchestration CLI surface. A static doctor cannot exercise this post-attach
check.

Only after explicit approval, the live doctor creates and destroys one
sandbox:

```powershell
orca vm recipe doctor local-docker-sandbox `
  --repo-path $PWD.Path `
  --provision `
  --json
```

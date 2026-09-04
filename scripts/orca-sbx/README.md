# Orca Docker Sandbox environment

This Windows-host recipe creates one Docker Sandbox per
`ORCA_VM_INSTANCE_ID`. It uses the standalone `sbx` CLI; Docker Desktop, a
host Docker Engine, and Podman are not dependencies. Each sandbox owns a
private Docker Engine with the Docker Compose v2 plugin, and the lifecycle
never detects, shims, or mounts a host Docker/Podman engine into the
sandbox. Orca connects through Docker's managed `<name>.sbx` SSH
`ProxyCommand` and retains schema-version-1, linked-worktree checkout
ownership because the recipe intentionally omits `checkoutMode`.

OMP is installed in the pinned worker image. Its canonical non-secret
configuration is committed under `.omp/` and linked into the sandbox's
user-level OMP location during provisioning. The host lifecycle reads the
OmniRoute key and registers a sandbox-scoped custom secret that Docker's
proxy injects only for `omniroute.tecklab.dk`; command failures are redacted.
The sandbox receives only the `proxy-managed` sentinel; the real key is never
written to the sandbox, repo, recipe JSON, or lifecycle logs.

Host key lookup precedence is: a non-empty `OMNIROUTE_API_KEY` environment
variable, then an explicit non-empty `ORCA_OMNIROUTE_ENV_FILE` path, then the
default host credential file `%USERPROFILE%\.config\teck\omniroute.env`
(built from the host home directory, `os.homedir()`). Only these host-only
sources are read; the lifecycle never falls back to a sibling repo checkout
such as a `Teck.Paseo/.env` next to this repository.

## Windows prerequisites

- Windows 11 on a 64-bit Intel or AMD CPU
- Windows Hypervisor Platform enabled
- standalone Docker Sandboxes CLI 0.39.0 or newer, installed with
  `winget install -h Docker.sbx`
- `sbx login` and `sbx setup ssh` completed
- Windows OpenSSH client and Node.js 22 or newer
- access to `ghcr.io/teck-lab/paseo-worker:omp18.0.4-bun1.4.0`
- outbound HTTPS access to `https://omniroute.tecklab.dk/v1`; `/v1/models`
  returns `401` without the key
- `OMNIROUTE_API_KEY` set in the environment, `ORCA_OMNIROUTE_ENV_FILE`
  pointing at a host-only env file, or the default host credential file
  `%USERPROFILE%\.config\teck\omniroute.env` containing the key

Docker's managed SSH block lives in `%USERPROFILE%\.ssh\config` and routes
`*.sbx` through the local `sbx ssh proxy` command. Start Orca with the system
SSH transport so this `ProxyCommand` is authoritative:

```powershell
$env:ORCA_SSH_FORCE_SYSTEM_TRANSPORT = '1'
orca open
```

The recipe verifies the managed SSH block before provisioning. Create and
resume readiness run inside the sandbox and verify OMP, its committed
configuration, the Docker CLI connected to the sandbox's private Docker
Engine, the Docker Compose v2 plugin, the proxy sentinel, and that the
proxied key authenticates against the public OmniRoute endpoint before
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

# Orca Docker Sandbox environment

This Windows-host recipe creates one Docker Sandbox per
`ORCA_VM_INSTANCE_ID`. It uses the standalone `sbx` CLI; Docker Desktop, a
host Docker Engine, and Podman are not dependencies. Each sandbox owns a
private Docker Engine with the Docker Compose v2 plugin, and the lifecycle
never detects, shims, or mounts a host Docker/Podman engine into the
sandbox. Orca connects through Docker's managed `<name>.sbx` SSH
`ProxyCommand` and retains schema-version-1, linked-worktree checkout
ownership because the recipe intentionally omits `checkoutMode`.

OMP and Bun are installed by the checked-in sandbox template at
`scripts/orca-sbx/template/Containerfile`. The lifecycle's pinned
`defaultImage` is the published build; `ORCA_SBX_IMAGE` can override that
reference for another published build. The separate kit under
`scripts/orca-sbx/kit/` applies runtime files and network policy.
Its canonical non-secret configuration is committed under `.omp/` and linked
into the sandbox's
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
- Gpg4win with a working personal signing key configured through
  `user.signingkey`, `gpg.program`, and `commit.gpgsign=true`
- GitHub CLI authenticated with `admin:gpg_key` while registering the dedicated
  sandbox signing key

## One-time host credential setup

Provision the default host credential file once per Windows machine. The
script securely prompts for the key, verifies it against the public OmniRoute
endpoint, and writes it to `%USERPROFILE%\.config\teck\omniroute.env` with a
locked ACL: inherited access is removed and only the current user (read and
write) plus the SYSTEM and Administrators principals can access the file:

```powershell
.\scripts\orca-sbx\setup-host.ps1
```

To provision from 1Password without typing the key, resolve an `op://`
reference with the 1Password CLI at setup time:

```powershell
.\scripts\orca-sbx\setup-host.ps1 `
  -SecretRef 'op://Teck/OmniRoute/api-key'
```

Re-run the script after rotating the key, then recreate already-existing
sandboxes. Every run replaces the file through a staged, atomic write - the
current user always keeps write access, an interrupted run leaves the
previous credential intact, and an existing file locked read-only by an older
setup-host.ps1 is repaired automatically. The file is the default host
credential source; `OMNIROUTE_API_KEY` and `ORCA_OMNIROUTE_ENV_FILE` take
precedence when set, and the lifecycle never consults any other location.

## GPG commit signing setup

Windows commits use the developer's existing GPG identity. Sandbox commits use
a separate sign-only GPG key so the personal private key never enters an
agent-controlled environment. The dedicated key has no passphrase so
non-interactive workers can sign; treat it as a constrained automation
credential. It expires after one year, is stored only in
`%USERPROFILE%\.config\teck\sandbox-signing-key.asc` under a locked ACL, and is
imported into each disposable sandbox during creation.

Grant GitHub CLI permission to register signing keys, then run the one-time
setup:

```powershell
gh auth refresh -h github.com -s admin:gpg_key
.\scripts\orca-sbx\setup-signing.ps1
```

The setup refuses to continue unless Windows can produce a signature with its
configured personal key. It creates or reuses the dedicated sandbox key,
registers the public key with the authenticated GitHub account, imports that
public key into the Windows keyring for integration verification, and proves
the dedicated private key can sign without interaction. Use `-Rotate` to
replace an expired or compromised sandbox key, then recreate every existing
sandbox.

`ORCA_GPG_SIGNING_KEY_FILE` may point the lifecycle at another host-only
armored private-key file. The lifecycle fails before returning a workspace when
the key is missing or unusable. Inside the sandbox it installs the key in a
dedicated GPG home, enables `commit.gpgsign`, and verifies an actual signature.
The post-attach runtime check repeats that proof.

The private key is intentionally available to processes inside each running
sandbox. It must never be committed, logged, added to recipe JSON, or reused
for encryption, certification, package signing, or personal commits.

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

Verify both host secret storage and GPG-aware lifecycle behavior:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\orca-sbx\host-secret.test.ps1
node --test scripts/orca-sbx/lifecycle.test.mjs
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

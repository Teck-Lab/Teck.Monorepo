#!/usr/bin/env node

import { spawn, spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import {
  chmodSync,
  copyFileSync,
  existsSync,
  mkdirSync,
  readFileSync,
  renameSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { homedir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
const pluginRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const defaultImage = "ghcr.io/teck-lab/paseo-worker:omp18.0.4-bun1.4.0";
const omniRouteHost = "omniroute.tecklab.dk";
const omniRouteBaseUrl = `https://${omniRouteHost}/v1`;
const sshPort = 2222;
const commandScripts =
  process.env.NODE_ENV === "test"
    ? {
        sbx: process.env.ORCA_SBX_SCRIPT,
        git: process.env.ORCA_GIT_SCRIPT,
        "ssh-keygen.exe": process.env.ORCA_SSH_KEYGEN_SCRIPT,
      }
    : {};

export function sandboxName(projectId) {
  if (!projectId?.trim()) throw new Error("ORCA_PROJECT_ID is required by the Teck sandbox recipe");
  const hash = createHash("sha256").update(projectId.trim()).digest("hex").slice(0, 12);
  return `orca-p-${hash}`;
}

export function sandboxState(name, homeDir = homedir()) {
  const directory = join(homeDir, ".orca-sbx", name);
  return {
    directory,
    workspace: join(directory, "workspace"),
    identityFile: join(directory, "id_ed25519"),
    publicKeyFile: join(directory, "id_ed25519.pub"),
    hostKeyFile: join(directory, "ssh_host_ed25519_key"),
    hostPublicKeyFile: join(directory, "ssh_host_ed25519_key.pub"),
    keepalivePidFile: join(directory, "keepalive.pid"),
    lockDirectory: `${directory}.lock`,
  };
}

export function deterministicPort(name) {
  const value = Number.parseInt(name.replace(/^orca-p-/, "").slice(0, 4), 16);
  if (!Number.isSafeInteger(value)) throw new Error(`Invalid project sandbox name: ${name}`);
  return 30000 + (value % 10000);
}

export function parsePublishedPort(output) {
  const entries = JSON.parse(output);
  if (!Array.isArray(entries)) throw new Error("Docker Sandbox returned invalid port JSON");
  const mapping = entries.find(
    (entry) =>
      entry?.host_ip === "127.0.0.1" &&
      entry.sandbox_port === sshPort &&
      typeof entry.protocol === "string" &&
      entry.protocol.startsWith("tcp") &&
      Number.isSafeInteger(entry.host_port) &&
      entry.host_port >= 1 &&
      entry.host_port <= 65535,
  );
  if (!mapping) throw new Error(`Docker Sandbox did not publish SSH port ${sshPort}`);
  return mapping.host_port;
}

export function parseIdentity(userOutput, homeOutput) {
  const username = userOutput.trim();
  const home = homeOutput.trim().replace(/\/$/, "");
  if (!/^[a-z_][a-z0-9_-]*$/i.test(username)) {
    throw new Error(
      `Docker Sandbox returned an invalid default username: ${username || "<empty>"}`,
    );
  }
  if (!/^\/[a-z0-9._/-]+$/i.test(home)) {
    throw new Error(`Docker Sandbox returned an invalid default home: ${home || "<empty>"}`);
  }
  return { username, home };
}

export function shellQuote(value) {
  return `'${value.replaceAll("'", `'"'"'`)}'`;
}

export function recipeResult(name, projectRoot, connection) {
  return {
    schemaVersion: 1,
    connection: {
      type: "ssh",
      projectRoot,
      target: {
        label: `Teck Docker Sandbox: ${name}`,
        host: "127.0.0.1",
        port: connection.port,
        username: connection.username,
        identityFile: connection.identityFile,
        identitiesOnly: true,
      },
    },
    userData: { provider: "teck-docker-sandbox", resourceId: name, projectRoot },
  };
}

export function wakeCheckCommand(home) {
  const gpgProgram = `${home}/.local/bin/orca-gpg`;
  return [
    "set -eu",
    "test -x /usr/local/bin/omp",
    "test -x /usr/local/bin/orca-runtime-check",
    `test -r ${shellQuote(`${home}/.omp/agent/config.yml`)}`,
    `test -r ${shellQuote(`${home}/.omp/agent/models.yml`)}`,
    `test -r ${shellQuote(`${home}/.omp/agent/RULES.md`)}`,
    `test -w ${shellQuote(`${home}/.omp/run`)}`,
    'test "${OMNIROUTE_API_KEY:-}" = proxy-managed',
    "omp --version >/dev/null",
    "docker info >/dev/null",
    "docker compose version >/dev/null",
    `curl -fsS -H 'Authorization: Bearer proxy-managed' ${omniRouteBaseUrl}/models >/dev/null`,
    'test -n "$(git config --global user.name)"',
    'test -n "$(git config --global user.email)"',
    'test "$(git config --global --bool commit.gpgsign)" = true',
    `test -x ${shellQuote(gpgProgram)}`,
    'test -n "$(git config --global user.signingkey)"',
    `printf 'teck-sandbox-signing-check' | ${shellQuote(gpgProgram)} --batch --pinentry-mode loopback --passphrase '' --yes --local-user "$(git config --global user.signingkey)" --output /dev/null --detach-sign`,
  ].join("; ");
}

export function signingCommand(identity) {
  const signingHome = `${identity.home}/.gnupg-orca-signing`;
  const localBin = `${identity.home}/.local/bin`;
  const gpgProgram = `${localBin}/orca-gpg`;
  return [
    "set -eu",
    `rm -rf ${shellQuote(signingHome)}`,
    `install -d -m 700 ${shellQuote(signingHome)} ${shellQuote(localBin)}`,
    `gpg --batch --homedir ${shellQuote(signingHome)} --import`,
    `fingerprint="$(gpg --batch --homedir ${shellQuote(signingHome)} --with-colons --list-secret-keys | awk -F: '$1 == "fpr" { print $10; exit }')"`,
    'test -n "$fingerprint"',
    `printf '%s\\n' '#!/usr/bin/env sh' 'exec gpg --homedir ${signingHome} "$@"' > ${shellQuote(gpgProgram)}`,
    `chmod 700 ${shellQuote(gpgProgram)}`,
    'git config --global user.signingkey "$fingerprint"',
    `git config --global gpg.program ${shellQuote(gpgProgram)}`,
    "git config --global commit.gpgsign true",
  ].join("; ");
}

function log(message) {
  process.stderr.write(`${message}\n`);
}

function redact(value, sensitive = []) {
  let output = String(value);
  for (const secret of sensitive) if (secret) output = output.replaceAll(secret, "<redacted>");
  return output;
}

function resolveCommand(command, args) {
  const script = commandScripts[command];
  return script ? { command: process.execPath, args: [script, ...args] } : { command, args };
}

function execute(command, args, options = {}) {
  const resolved = resolveCommand(command, args);
  return spawnSync(resolved.command, resolved.args, {
    encoding: "utf8",
    windowsHide: true,
    stdio: options.capture
      ? [options.input === undefined ? "ignore" : "pipe", "pipe", "pipe"]
      : [options.input === undefined ? "ignore" : "pipe", 2, 2],
    input: options.input,
  });
}

function run(command, args, options = {}) {
  const result = execute(command, args, options);
  const sensitive = options.sensitive ?? [];
  if (result.error)
    throw new Error(redact(`${command} could not start: ${result.error.message}`, sensitive));
  if (result.status !== 0) {
    const detail = options.capture
      ? redact(result.stderr || result.stdout || "", sensitive).trim()
      : "";
    throw new Error(
      `${[command, ...args].map((part) => redact(part, sensitive)).join(" ")} failed with exit code ${result.status}${detail ? `: ${detail}` : ""}`,
    );
  }
  return options.capture ? result.stdout : "";
}

function runOptional(command, args) {
  const result = execute(command, args, { capture: true });
  if (result.error) throw result.error;
  return result.status === 0 ? result.stdout.trim() : "";
}

function required(name) {
  const value = process.env[name]?.trim();
  if (!value) throw new Error(`${name} is required by the Teck sandbox recipe`);
  return value;
}

function readPayload() {
  const raw = readFileSync(0, "utf8");
  if (!raw.trim()) throw new Error("Orca lifecycle payload was empty");
  const payload = JSON.parse(raw);
  const userData = payload.recipeResult?.userData ?? payload.userData;
  if (!/^orca-p-[0-9a-f]{12}$/.test(userData?.resourceId ?? "")) {
    throw new Error("Lifecycle payload has no valid project sandbox id");
  }
  return { resourceId: userData.resourceId, projectRoot: userData.projectRoot };
}

function configuredApiKey() {
  if (process.env.OMNIROUTE_API_KEY?.trim()) return process.env.OMNIROUTE_API_KEY.trim();
  const candidates = [
    process.env.ORCA_OMNIROUTE_ENV_FILE?.trim(),
    join(homedir(), ".config", "teck", "omniroute.env"),
  ].filter(Boolean);
  for (const path of candidates) {
    if (!existsSync(path)) continue;
    const line = readFileSync(path, "utf8")
      .split(/\r?\n/)
      .find((value) => /^\s*OMNIROUTE_API_KEY=/.test(value));
    const value = line
      ?.replace(/^\s*OMNIROUTE_API_KEY=/, "")
      .trim()
      .replace(/^(['"])(.*)\1$/, "$2");
    if (value && !value.startsWith("change-me")) return value;
  }
  throw new Error("OmniRoute key not found; run scripts/orca-sbx/setup-host.ps1");
}

function configuredSigningKey() {
  const path =
    process.env.ORCA_GPG_SIGNING_KEY_FILE?.trim() ||
    join(homedir(), ".config", "teck", "sandbox-signing-key.asc");
  const value = existsSync(path) ? readFileSync(path, "utf8").trim() : "";
  if (!value.includes("-----BEGIN PGP PRIVATE KEY BLOCK-----")) {
    throw new Error(
      "Dedicated sandbox signing key not found; run scripts/orca-sbx/setup-signing.ps1",
    );
  }
  return value;
}

function sandboxExists(name) {
  return run("sbx", ["ls", "--quiet"], { capture: true })
    .split(/\r?\n/)
    .map((value) => value.trim())
    .includes(name);
}

function processAlive(pid) {
  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}

function acquireLock(state) {
  const deadline = Date.now() + Number(process.env.ORCA_SBX_LOCK_TIMEOUT || 600) * 1000;
  while (true) {
    try {
      mkdirSync(state.lockDirectory);
      writeFileSync(join(state.lockDirectory, "pid"), String(process.pid));
      return () => rmSync(state.lockDirectory, { recursive: true, force: true });
    } catch (error) {
      if (error.code !== "EEXIST") throw error;
      const pidFile = join(state.lockDirectory, "pid");
      const holder = existsSync(pidFile) ? Number(readFileSync(pidFile, "utf8")) : 0;
      if (holder && !processAlive(holder)) {
        const stealDirectory = `${state.lockDirectory}.steal`;
        let ownsStealLock = false;
        try {
          mkdirSync(stealDirectory);
          ownsStealLock = true;
          const currentHolder = existsSync(pidFile) ? Number(readFileSync(pidFile, "utf8")) : 0;
          if (currentHolder === holder) {
            rmSync(state.lockDirectory, { recursive: true, force: true });
          }
        } catch (stealError) {
          if (stealError.code !== "EEXIST") throw stealError;
        } finally {
          if (ownsStealLock) rmSync(stealDirectory, { recursive: true, force: true });
        }
        continue;
      }
      if (Date.now() >= deadline) throw new Error(`Timed out waiting for ${state.lockDirectory}`);
      Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 1000);
    }
  }
}

function ensureHostState(name) {
  const state = sandboxState(name);
  mkdirSync(state.workspace, { recursive: true, mode: 0o700 });
  chmodSync(state.directory, 0o700);
  if (!existsSync(state.identityFile)) {
    run("ssh-keygen.exe", ["-q", "-t", "ed25519", "-f", state.identityFile, "-N", ""], {
      capture: true,
    });
  }
  return state;
}

function pluginKitPath() {
  const path = join(pluginRoot, "kit");
  if (!existsSync(join(path, "spec.yaml"))) {
    throw new Error(`Teck sandbox plugin kit is missing: ${path}`);
  }
  return path;
}

function resolveIdentity(name) {
  return parseIdentity(
    run("sbx", ["exec", name, "id", "-un"], { capture: true }),
    run("sbx", ["exec", name, "sh", "-lc", 'printf "%s" "$HOME"'], { capture: true }),
  );
}

function writeSandboxFile(name, user, destination, content, mode = "600") {
  run(
    "sbx",
    [
      "exec",
      "-i",
      "-u",
      user,
      name,
      "sh",
      "-lc",
      `umask 077; cat > ${shellQuote(destination)}; chmod ${mode} ${shellQuote(destination)}`,
    ],
    { capture: true, input: content },
  );
}

export function sshdPrerequisiteCommand() {
  return "set -eu; command -v g++ >/dev/null 2>&1 && test -x /usr/sbin/sshd || { attempt=0; while pgrep -x apt-get >/dev/null 2>&1 || pgrep -x dpkg >/dev/null 2>&1; do attempt=$((attempt + 1)); test $attempt -lt 60 || { echo 'timed out waiting for sandbox apt startup job' >&2; exit 100; }; sleep 5; done; apt-get -o DPkg::Lock::Timeout=300 update -qq; DEBIAN_FRONTEND=noninteractive apt-get -o DPkg::Lock::Timeout=300 install -y -qq build-essential openssh-server; }; ssh-keygen -A; install -d -m 755 /run/sshd";
}

function ensureSshd(name, identity, state) {
  run("sbx", ["exec", "-u", "0", name, "sh", "-lc", sshdPrerequisiteCommand()], {
    capture: true,
  });

  if (existsSync(state.hostKeyFile) && existsSync(state.hostPublicKeyFile)) {
    writeSandboxFile(
      name,
      "0",
      "/etc/ssh/ssh_host_ed25519_key",
      readFileSync(state.hostKeyFile, "utf8"),
    );
    writeSandboxFile(
      name,
      "0",
      "/etc/ssh/ssh_host_ed25519_key.pub",
      readFileSync(state.hostPublicKeyFile, "utf8"),
      "644",
    );
  } else {
    writeFileSync(
      state.hostKeyFile,
      run("sbx", ["exec", "-u", "0", name, "cat", "/etc/ssh/ssh_host_ed25519_key"], {
        capture: true,
      }),
      { mode: 0o600 },
    );
    writeFileSync(
      state.hostPublicKeyFile,
      run("sbx", ["exec", "-u", "0", name, "cat", "/etc/ssh/ssh_host_ed25519_key.pub"], {
        capture: true,
      }),
      { mode: 0o600 },
    );
  }

  const authorizedKeys = `${identity.home}/.ssh/authorized_keys`;
  run(
    "sbx",
    [
      "exec",
      "-i",
      "-u",
      identity.username,
      name,
      "sh",
      "-lc",
      `set -eu; install -d -m 700 ${shellQuote(`${identity.home}/.ssh`)}; touch ${shellQuote(authorizedKeys)}; chmod 600 ${shellQuote(authorizedKeys)}; key="$(cat)"; grep -qF "$key" ${shellQuote(authorizedKeys)} || printf '%s\\n' "$key" >> ${shellQuote(authorizedKeys)}`,
    ],
    { capture: true, input: readFileSync(state.publicKeyFile, "utf8").trim() },
  );

  run(
    "sbx",
    [
      "exec",
      "-u",
      "0",
      name,
      "sh",
      "-lc",
      `pgrep -x sshd >/dev/null 2>&1 || /usr/sbin/sshd -E /tmp/orca-sshd.log -p ${sshPort} -o HostKey=/etc/ssh/ssh_host_ed25519_key`,
    ],
    { capture: true },
  );
}

function currentPublishedPort(name) {
  try {
    return parsePublishedPort(run("sbx", ["ports", name, "--json"], { capture: true }));
  } catch {
    return undefined;
  }
}

function ensurePublishedPort(name) {
  const current = currentPublishedPort(name);
  if (current) return current;
  const base = deterministicPort(name);
  for (let offset = 0; offset < 10; offset += 1) {
    const hostPort = 30000 + ((base - 30000 + offset) % 10000);
    const result = execute("sbx", ["ports", name, "--publish", `${hostPort}:${sshPort}`], {
      capture: true,
    });
    if (!result.error && result.status === 0) return currentPublishedPort(name);
    log(`[PORT] ${hostPort} unavailable for ${name}`);
  }
  throw new Error(`Could not publish SSH port ${sshPort} for ${name}`);
}

export function reconcileKnownHost(port, publicKey, homeDir = homedir()) {
  if (!Number.isSafeInteger(port) || port < 1 || port > 65535) {
    throw new Error(`Invalid SSH host port: ${port}`);
  }
  const host = `[127.0.0.1]:${port}`;
  const knownHosts = join(homeDir, ".ssh", "known_hosts");
  const temporary = `${knownHosts}.${process.pid}.tmp`;
  mkdirSync(dirname(knownHosts), { recursive: true });
  const release = acquireLock({ lockDirectory: `${knownHosts}.lock` });
  try {
    if (existsSync(knownHosts)) copyFileSync(knownHosts, temporary);
    else writeFileSync(temporary, "", { mode: 0o600 });
    const removal = execute("ssh-keygen.exe", ["-R", host, "-f", temporary], {
      capture: true,
    });
    if (removal.error || ![0, 1].includes(removal.status)) {
      throw new Error(`Could not reconcile SSH host trust for ${host}`);
    }
    const key = publicKey.trim().split(/\s+/).slice(0, 2).join(" ");
    if (!/^ssh-[a-z0-9-]+ [A-Za-z0-9+/=]+$/.test(key)) {
      throw new Error("Persisted Docker Sandbox SSH host key is invalid");
    }
    const current = readFileSync(temporary, "utf8").replace(/\s*$/, "");
    writeFileSync(temporary, `${current ? `${current}\n` : ""}${host} ${key}\n`, { mode: 0o600 });
    chmodSync(temporary, 0o600);
    renameSync(temporary, knownHosts);
  } finally {
    rmSync(temporary, { force: true });
    rmSync(`${temporary}.old`, { force: true });
    release();
  }
}

function spawnResolved(command, args, options) {
  const resolved = resolveCommand(command, args);
  return spawn(resolved.command, resolved.args, options);
}

function ensureKeepalive(name, state) {
  if (existsSync(state.keepalivePidFile)) {
    const pid = Number(readFileSync(state.keepalivePidFile, "utf8"));
    if (Number.isSafeInteger(pid) && processAlive(pid)) return;
  }
  const child = spawnResolved("sbx", ["exec", name, "sleep", "2147483647"], {
    detached: true,
    stdio: "ignore",
    windowsHide: true,
  });
  child.unref();
  writeFileSync(state.keepalivePidFile, String(child.pid));
}

function stopKeepalive(state) {
  if (!existsSync(state.keepalivePidFile)) return;
  const pid = Number(readFileSync(state.keepalivePidFile, "utf8"));
  if (Number.isSafeInteger(pid) && processAlive(pid)) {
    try {
      process.kill(pid);
    } catch {
      // The process exited after the liveness check.
    }
  }
  rmSync(state.keepalivePidFile, { force: true });
}

function configureSecret(name) {
  const key = configuredApiKey();
  run("sbx", ["secret", "rm", "--sandbox", name, "--placeholder", "proxy-managed", "--force"], {
    capture: true,
  });
  run(
    "sbx",
    [
      "secret",
      "set-custom",
      "--sandbox",
      name,
      "--host",
      omniRouteHost,
      "--env",
      "OMNIROUTE_API_KEY",
      "--placeholder",
      "proxy-managed",
      "--value",
      key,
    ],
    { capture: true, sensitive: [key] },
  );
}

export function requiredTeckPaths() {
  return [".omp/config.yml", ".omp/models.yml", ".omp/RULES.md"];
}

export function validateTeckRepo(repoRoot) {
  const packagePath = join(repoRoot, "package.json");
  if (!existsSync(packagePath)) throw new Error("Teck sandbox recipe requires package.json");
  const packageJson = JSON.parse(readFileSync(packagePath, "utf8"));
  if (packageJson.name !== "teck-platform") {
    throw new Error("Teck sandbox recipe can only provision Teck.Monorepo");
  }
  const missing = requiredTeckPaths().filter((path) => !existsSync(join(repoRoot, path)));
  if (missing.length > 0) throw new Error(`Teck sandbox recipe is missing: ${missing.join(", ")}`);
}

function ensureProjectClone(name, identity, repoRoot) {
  const projectRoot = `${identity.home}/project`;
  const probe = execute("sbx", ["exec", name, "test", "-d", `${projectRoot}/.git`], {
    capture: true,
  });
  if (probe.error) throw probe.error;
  if (probe.status === 0) return projectRoot;
  if (probe.status !== 1) throw new Error(`Could not inspect ${projectRoot} in ${name}`);
  const repoUrl =
    process.env.ORCA_REPO_URL?.trim() ||
    run("git", ["-C", repoRoot, "remote", "get-url", "origin"], { capture: true }).trim();
  run("sbx", ["exec", "-u", identity.username, name, "git", "clone", "--", repoUrl, projectRoot], {
    capture: true,
  });
  return projectRoot;
}

export function teckConfigPaths(identity, projectRoot) {
  const targetRoot = `${identity.home}/.omp/agent`;
  return ["config.yml", "models.yml", "RULES.md"].map((file) => ({
    source: `${projectRoot}/.omp/${file}`,
    destination: `${targetRoot}/${file}`,
  }));
}

export function gitAuthorConfigArgs(name, identity, key, value) {
  if (!value) throw new Error(`Host Git ${key} is missing`);
  return ["exec", "-u", identity.username, name, "git", "config", "--global", key, value];
}

function provisionTeck(name, identity, projectRoot, repoRoot) {
  const ompAgent = `${identity.home}/.omp/agent`;
  run(
    "sbx",
    [
      "exec",
      "-u",
      identity.username,
      name,
      "sh",
      "-lc",
      `set -eu; install -d -m 700 ${shellQuote(ompAgent)} ${shellQuote(`${identity.home}/.omp/run`)} ${shellQuote(`${identity.home}/.local/bin`)}`,
    ],
    { capture: true },
  );
  for (const path of teckConfigPaths(identity, projectRoot)) {
    const file = path.source.slice(path.source.lastIndexOf("/") + 1);
    writeSandboxFile(
      name,
      identity.username,
      path.destination,
      readFileSync(join(repoRoot, ".omp", file), "utf8"),
      "600",
    );
  }
  run(
    "sbx",
    [
      "exec",
      "-u",
      "0",
      name,
      "install",
      "-m",
      "755",
      "/home/agent/.local/bin/orca-runtime-check",
      "/usr/local/bin/orca-runtime-check",
    ],
    { capture: true },
  );
  const signingKey = configuredSigningKey();
  run("sbx", ["exec", "-i", "-u", identity.username, name, "sh", "-lc", signingCommand(identity)], {
    capture: true,
    input: signingKey,
    sensitive: [signingKey],
  });
  const authorName = runOptional("git", ["-C", repoRoot, "config", "user.name"]);
  const authorEmail = runOptional("git", ["-C", repoRoot, "config", "user.email"]);
  if (!authorName || !authorEmail) {
    throw new Error(
      "Host Git author is missing; configure user.name and user.email for this repository",
    );
  }
  run("sbx", gitAuthorConfigArgs(name, identity, "user.name", authorName), { capture: true });
  run("sbx", gitAuthorConfigArgs(name, identity, "user.email", authorEmail), {
    capture: true,
  });
}

function emit(result) {
  process.stdout.write(`${JSON.stringify(result)}\n`);
}
function verifyRuntime(name, identity) {
  run(
    "sbx",
    ["exec", "-u", identity.username, name, "sh", "-lc", wakeCheckCommand(identity.home)],
    {
      capture: true,
    },
  );
}

function create() {
  if (process.platform !== "win32") throw new Error("This Teck plugin lifecycle requires Windows");
  run("sbx", ["version"], { capture: true });
  const repoRoot = resolve(required("ORCA_REPO_PATH"));
  validateTeckRepo(repoRoot);
  const name = sandboxName(required("ORCA_PROJECT_ID"));
  const state = ensureHostState(name);
  const release = acquireLock(state);
  let created = false;
  try {
    if (!sandboxExists(name)) {
      run("sbx", [
        "create",
        "--name",
        name,
        "--cpus",
        process.env.ORCA_SBX_CPUS || "4",
        "--memory",
        process.env.ORCA_SBX_MEMORY || "4g",
        "--kit",
        pluginKitPath(),
        "--template",
        process.env.ORCA_SBX_IMAGE || defaultImage,
        "shell",
        state.workspace,
      ]);
      created = true;
    } else {
      run("sbx", ["exec", name, "true"], { capture: true });
    }
    configureSecret(name);
    const identity = resolveIdentity(name);
    const projectRoot = ensureProjectClone(name, identity, repoRoot);
    provisionTeck(name, identity, projectRoot, repoRoot);
    ensureSshd(name, identity, state);
    const connection = {
      username: identity.username,
      identityFile: state.identityFile,
      port: ensurePublishedPort(name),
    };
    reconcileKnownHost(connection.port, readFileSync(state.hostPublicKeyFile, "utf8"));
    ensureKeepalive(name, state);
    verifyRuntime(name, identity);
    emit(recipeResult(name, projectRoot, connection));
  } catch (error) {
    if (created && process.env.ORCA_SBX_KEEP_FAILED !== "1") {
      stopKeepalive(state);
      try {
        run("sbx", ["rm", "--force", name]);
      } catch (cleanupError) {
        log(`[WARN] cleanup failed: ${cleanupError.message}`);
      }
    } else if (created) {
      log(`[DEBUG] preserving failed sandbox ${name} because ORCA_SBX_KEEP_FAILED=1`);
    }
    throw error;
  } finally {
    release();
  }
}

function suspend() {
  const { resourceId } = readPayload();
  log(`[SUSPEND] ${resourceId}: shared project sandbox remains active`);
}

function resume() {
  const { resourceId } = readPayload();
  const state = ensureHostState(resourceId);
  const release = acquireLock(state);
  try {
    if (!sandboxExists(resourceId))
      throw new Error(`Shared sandbox ${resourceId} no longer exists`);
    run("sbx", ["exec", resourceId, "true"], { capture: true });
    const identity = resolveIdentity(resourceId);
    const projectRoot = `${identity.home}/project`;
    ensureSshd(resourceId, identity, state);
    const connection = {
      username: identity.username,
      identityFile: state.identityFile,
      port: ensurePublishedPort(resourceId),
    };
    reconcileKnownHost(connection.port, readFileSync(state.hostPublicKeyFile, "utf8"));
    ensureKeepalive(resourceId, state);
    verifyRuntime(resourceId, identity);
    emit(recipeResult(resourceId, projectRoot, connection));
  } finally {
    release();
  }
}

function destroy() {
  const { resourceId } = readPayload();
  const state = sandboxState(resourceId);
  mkdirSync(state.directory, { recursive: true });
  const release = acquireLock(state);
  try {
    if (!sandboxExists(resourceId)) {
      stopKeepalive(state);
      rmSync(state.directory, { recursive: true, force: true });
      return;
    }
    const identity = resolveIdentity(resourceId);
    const projectRoot = `${identity.home}/project`;
    run("sbx", ["exec", resourceId, "git", "-C", projectRoot, "worktree", "prune"], {
      capture: true,
    });
    const worktrees = run(
      "sbx",
      ["exec", resourceId, "git", "-C", projectRoot, "worktree", "list", "--porcelain"],
      { capture: true },
    )
      .split(/\r?\n/)
      .filter((line) => line.startsWith("worktree "));
    if (worktrees.length > 1) {
      log(`[DESTROY] ${resourceId}: ${worktrees.length - 1} sibling workspace(s) remain`);
      return;
    }
    stopKeepalive(state);
    run(
      "sbx",
      ["secret", "rm", "--sandbox", resourceId, "--placeholder", "proxy-managed", "--force"],
      {
        capture: true,
      },
    );
    run("sbx", ["rm", "--force", resourceId]);
    rmSync(state.directory, { recursive: true, force: true });
  } finally {
    release();
  }
}

const actions = { create, suspend, resume, destroy };
const action = process.argv[2];
if (action && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try {
    if (!actions[action]) throw new Error(`Unknown lifecycle action: ${action}`);
    actions[action]();
  } catch (error) {
    log(`[ERROR] ${error.message}`);
    process.exitCode = 1;
  }
}

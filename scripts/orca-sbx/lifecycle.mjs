#!/usr/bin/env node

import { spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { homedir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const scriptDir = dirname(fileURLToPath(import.meta.url));
const defaultImage = "ghcr.io/teck-lab/paseo-worker:omp18.0.4-bun1.4.0";

const omniRouteHost = "omniroute.tecklab.dk";
const omniRouteBaseUrl = `https://${omniRouteHost}/v1`;

export function sandboxName(recipeId, instanceId) {
  const clean = (value) =>
    value
      .toLowerCase()
      .replace(/[^a-z0-9-]+/g, "-")
      .replace(/^-+|-+$/g, "");
  const suffix = clean(instanceId);
  if (!suffix) throw new Error("ORCA_VM_INSTANCE_ID is missing or invalid");
  const prefix = `orca-${clean(recipeId || "local-docker-sandbox") || "local-docker-sandbox"}`;
  const maxPrefix = 63 - suffix.length - 1;
  if (maxPrefix < 5) throw new Error("ORCA_VM_INSTANCE_ID is too long for a Docker Sandbox name");
  return `${prefix.slice(0, maxPrefix).replace(/-+$/g, "")}-${suffix}`;
}

export function remoteWindowsPath(path) {
  const match = /^([a-zA-Z]):[\\/](.*)$/.exec(path);
  if (!match)
    throw new Error(`Docker Sandbox workspace must be an absolute Windows drive path: ${path}`);
  return `/${match[1].toLowerCase()}/${match[2].replaceAll("\\", "/")}`.replace(/\/$/, "");
}

export function recipeResult(name, projectRoot) {
  return {
    schemaVersion: 1,
    connection: {
      type: "ssh",
      projectRoot,
      target: {
        label: `Docker Sandbox: ${name}`,
        host: `${name}.sbx`,
        port: 22,
        username: "_default_user_",
      },
    },
    userData: { provider: "docker-sandbox", resourceId: name, projectRoot },
  };
}

export function shellQuote(value) {
  return `'${value.replaceAll("'", `'"'"'`)}'`;
}

function log(message) {
  process.stderr.write(`${message}\n`);
}

export function redactSensitive(value, sensitiveValues = []) {
  let redacted = String(value);
  for (const sensitiveValue of sensitiveValues) {
    if (sensitiveValue) redacted = redacted.replaceAll(sensitiveValue, "<redacted>");
  }
  return redacted;
}

function run(command, args, options = {}) {
  const result = spawnSync(command, args, {
    encoding: "utf8",
    windowsHide: true,
    stdio: options.capture
      ? [options.input === undefined ? "ignore" : "pipe", "pipe", "pipe"]
      : [options.input === undefined ? "ignore" : "pipe", 2, 2],
    input: options.input,
  });
  const sensitiveValues = options.sensitive ?? [];
  if (result.error) {
    throw new Error(
      redactSensitive(`${command} could not be started: ${result.error.message}`, sensitiveValues),
    );
  }
  if (result.status !== 0) {
    const detail = options.capture
      ? redactSensitive(`${result.stderr || result.stdout || ""}`.trim(), sensitiveValues)
      : "";
    const invocation = [command, ...args]
      .map((part) => redactSensitive(part, sensitiveValues))
      .join(" ");
    throw new Error(
      `${invocation} failed with exit code ${result.status}${detail ? `: ${detail}` : ""}`,
    );
  }
  return options.capture ? result.stdout : "";
}

function required(name) {
  const value = process.env[name]?.trim();
  if (!value) throw new Error(`${name} is required by the Docker Sandbox recipe`);
  return value;
}
export function supportsSbxVersion(output) {
  const match = /\bv?(\d+)\.(\d+)\.(\d+)\b/i.exec(output);
  if (!match) return false;
  const major = Number(match[1]);
  const minor = Number(match[2]);
  return major > 0 || (major === 0 && minor >= 39);
}

function verifyHostPrerequisites() {
  const version = run("sbx", ["version"], { capture: true });
  if (!supportsSbxVersion(version)) {
    throw new Error(`Docker Sandboxes 0.39.0 or newer is required; found: ${version.trim()}`);
  }
  const sshConfig = run("ssh.exe", ["-G", "orca-probe.sbx"], { capture: true });
  if (!/^proxycommand\s+.*sbx(?:\.exe)?"?\s+ssh\s+proxy\s+%[nh]\s*$/im.test(sshConfig)) {
    throw new Error("Docker-managed SSH configuration is missing; run `sbx setup ssh`");
  }
}

function lifecyclePayload() {
  const raw = readFileSync(0, "utf8");
  if (!raw.trim()) throw new Error("Orca lifecycle payload was empty");
  const payload = JSON.parse(raw);
  const userData = payload.recipeResult?.userData ?? payload.userData;
  const resourceId = userData?.resourceId;
  if (!resourceId || !/^orca-[a-z0-9-]+$/.test(resourceId)) {
    throw new Error("Lifecycle payload has no valid Orca Docker Sandbox resource id");
  }
  return { resourceId, projectRoot: userData?.projectRoot };
}

export function configuredApiKey({ homeDir = homedir() } = {}) {
  if (process.env.OMNIROUTE_API_KEY?.trim()) return process.env.OMNIROUTE_API_KEY.trim();
  const explicitEnvFile = process.env.ORCA_OMNIROUTE_ENV_FILE?.trim();
  const defaultEnvFile = join(homeDir, ".config", "teck", "omniroute.env");
  for (const path of [explicitEnvFile, defaultEnvFile].filter(Boolean)) {
    try {
      const line = readFileSync(path, "utf8")
        .split(/\r?\n/)
        .find((entry) => /^\s*OMNIROUTE_API_KEY=/.test(entry));
      const value = line
        ?.replace(/^\s*OMNIROUTE_API_KEY=/, "")
        .trim()
        .replace(/^(['"])(.*)\1$/, "$2");
      if (value && !value.startsWith("change-me")) return value;
    } catch (error) {
      if (error.code !== "ENOENT") throw error;
    }
  }
  throw new Error(
    `OmniRoute key not found. Set OMNIROUTE_API_KEY, point ORCA_OMNIROUTE_ENV_FILE at a host-only env file, or store the key in ${defaultEnvFile}; secrets are never stored in recipe state`,
  );
}

function sandboxExists(name) {
  const output = run("sbx", ["ls", "--quiet"], { capture: true });
  return output
    .split(/\r?\n/)
    .map((line) => line.trim())
    .includes(name);
}

export function customSecretTargetHosts() {
  return [omniRouteHost];
}

export function wakeCheckCommand() {
  return [
    "set -eu",
    "test -x /usr/local/bin/omp",
    "test -x /home/agent/.local/bin/orca-runtime-check",
    "test -r /home/agent/.omp/agent/config.yml",
    "test -r /home/agent/.omp/agent/models.yml",
    "test -r /home/agent/.omp/agent/RULES.md",
    'test "${OMNIROUTE_API_KEY:-}" = proxy-managed',
    "omp --version >/dev/null",
    "docker info >/dev/null",
    "docker compose version >/dev/null",
    `curl -fsS -H 'Authorization: Bearer proxy-managed' ${omniRouteBaseUrl}/models >/dev/null`,
  ].join("; ");
}

function wakeAndVerify(name) {
  run("ssh.exe", [
    "-T",
    "-o",
    "BatchMode=yes",
    "-o",
    "LogLevel=ERROR",
    "--",
    `${name}.sbx`,
    "sh",
    "-lc",
    wakeCheckCommand(),
  ]);
}

function removeCustomSecret(name) {
  run("sbx", [
    "secret",
    "rm",
    "--sandbox",
    name,
    "--placeholder",
    "proxy-managed",
    "--force",
  ]);
}

function emit(result) {
  process.stdout.write(`${JSON.stringify(result)}\n`);
}

function create() {
  if (process.platform !== "win32")
    throw new Error("local-docker-sandbox must run on the Windows host that owns Docker Sandboxes");
  verifyHostPrerequisites();
  const repoRoot = resolve(required("ORCA_REPO_PATH"));
  const name = sandboxName(process.env.ORCA_RECIPE_ID, required("ORCA_VM_INSTANCE_ID"));
  const projectRoot = remoteWindowsPath(repoRoot);
  let created = false;
  let secretTouched = false;
  try {
    if (!sandboxExists(name)) {
      log(`[CREATE] ${name} -> ${repoRoot}`);
      run("sbx", [
        "create",
        "--name",
        name,
        "--cpus",
        process.env.ORCA_SBX_CPUS || "4",
        "--memory",
        process.env.ORCA_SBX_MEMORY || "4g",
        "--kit",
        join(scriptDir, "kit"),
        "--template",
        process.env.ORCA_SBX_IMAGE || defaultImage,
        "shell",
        repoRoot,
      ]);
      created = true;
    } else {
      log(`[REUSE] ${name}`);
    }

    const key = configuredApiKey();
    removeCustomSecret(name);
    secretTouched = true;
    const secretArgs = customSecretTargetHosts().flatMap((host) => ["--host", host]);
    run(
      "sbx",
      [
        "secret",
        "set-custom",
        "--sandbox",
        name,
        ...secretArgs,
        "--env",
        "OMNIROUTE_API_KEY",
        "--placeholder",
        "proxy-managed",
        "--value",
        key,
      ],
      { capture: true, sensitive: [key] },
    );

    const ompRoot = `${projectRoot}/.omp`;
    const command = `set -eu; install -d -o 1000 -g 1000 /home/agent/.omp/agent; ln -sfn ${shellQuote(`${ompRoot}/config.yml`)} /home/agent/.omp/agent/config.yml; ln -sfn ${shellQuote(`${ompRoot}/models.yml`)} /home/agent/.omp/agent/models.yml; ln -sfn ${shellQuote(`${ompRoot}/RULES.md`)} /home/agent/.omp/agent/RULES.md`;
    run("sbx", ["exec", "-u", "0", name, "sh", "-lc", command], { capture: true });
    wakeAndVerify(name);
    emit(recipeResult(name, projectRoot));
  } catch (error) {
    if (secretTouched) {
      try {
        removeCustomSecret(name);
      } catch (cleanupError) {
        log(`[WARN] custom secret cleanup failed: ${cleanupError.message}`);
      }
    }
    if (created) {
      try {
        run("sbx", ["rm", "--force", name]);
      } catch (cleanupError) {
        log(`[WARN] sandbox cleanup failed: ${cleanupError.message}`);
      }
    }
    throw error;
  }
}

function suspend() {
  const { resourceId } = lifecyclePayload();
  log(`[SUSPEND] ${resourceId}`);
  run("sbx", ["stop", resourceId]);
}

function resume() {
  const { resourceId, projectRoot } = lifecyclePayload();
  if (!projectRoot) throw new Error("Lifecycle payload is missing the remote project root");
  log(`[RESUME] ${resourceId}`);
  wakeAndVerify(resourceId);
  emit(recipeResult(resourceId, projectRoot));
}

function destroy() {
  const { resourceId } = lifecyclePayload();
  log(`[DESTROY] ${resourceId}`);
  removeCustomSecret(resourceId);
  run("sbx", ["rm", "--force", resourceId]);
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

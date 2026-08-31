#!/usr/bin/env node

import { spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const scriptDir = dirname(fileURLToPath(import.meta.url));
const defaultImage = "ghcr.io/teck-lab/paseo-worker:omp18.0.4-bun1.4.0";

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

export function redactedCommand(command, args) {
  const safe = [];
  let redactNext = false;
  for (const arg of args) {
    if (redactNext) {
      safe.push("<redacted>");
      redactNext = false;
      continue;
    }
    safe.push(arg);
    redactNext = arg === "--value" || arg === "--password" || arg === "--token";
  }
  return `${command} ${safe.join(" ")}`;
}

function log(message) {
  process.stderr.write(`${message}\n`);
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
  if (result.error) throw new Error(`${command} could not be started: ${result.error.message}`);
  if (result.status !== 0) {
    const detail = options.capture ? `${result.stderr || result.stdout || ""}`.trim() : "";
    throw new Error(
      `${redactedCommand(command, args)} failed with exit code ${result.status}${detail ? `: ${detail}` : ""}`,
    );
  }
  return options.capture ? result.stdout : "";
}

function required(name) {
  const value = process.env[name]?.trim();
  if (!value) throw new Error(`${name} is required by the Docker Sandbox recipe`);
  return value;
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

function configuredApiKey(repoRoot) {
  if (process.env.OMNIROUTE_API_KEY?.trim()) return process.env.OMNIROUTE_API_KEY.trim();
  const candidates = [
    process.env.ORCA_OMNIROUTE_ENV_FILE,
    join(scriptDir, ".env"),
    join(dirname(repoRoot), "Teck.Paseo", ".env"),
  ].filter(Boolean);
  for (const path of candidates) {
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
    "OmniRoute key not found. Set OMNIROUTE_API_KEY or ORCA_OMNIROUTE_ENV_FILE; secrets are never stored in recipe state",
  );
}

function sandboxExists(name) {
  const output = run("sbx", ["ls", "--quiet"], { capture: true });
  return output
    .split(/\r?\n/)
    .map((line) => line.trim())
    .includes(name);
}

function wakeAndVerify(name) {
  const check =
    "set -eu; test -x /usr/local/bin/omp; test -r /home/agent/.omp/agent/config.yml; test -r /home/agent/.omp/agent/models.yml; omp --version >/dev/null; curl -fsS -H 'Authorization: Bearer proxy-managed' http://host.docker.internal:20128/v1/models >/dev/null";
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
    check,
  ]);
}

function emit(result) {
  process.stdout.write(`${JSON.stringify(result)}\n`);
}

function create() {
  if (process.platform !== "win32")
    throw new Error("local-docker-sandbox must run on the Windows host that owns Docker Sandboxes");
  const repoRoot = resolve(required("ORCA_REPO_PATH"));
  const name = sandboxName(process.env.ORCA_RECIPE_ID, required("ORCA_VM_INSTANCE_ID"));
  const projectRoot = remoteWindowsPath(repoRoot);
  let created = false;
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

    const key = configuredApiKey(repoRoot);
    run("sbx", ["secret", "rm", "--sandbox", name, "--placeholder", "proxy-managed", "--force"]);
    run("sbx", [
      "secret",
      "set-custom",
      "--sandbox",
      name,
      "--host",
      "host.docker.internal",
      "--host",
      "localhost",
      "--env",
      "OMNIROUTE_API_KEY",
      "--placeholder",
      "proxy-managed",
      "--value",
      key,
    ]);

    const ompRoot = `${projectRoot}/.omp`;
    const command = `set -eu; install -d -o 1000 -g 1000 /home/agent/.omp/agent; ln -sfn ${shellQuote(`${ompRoot}/config.yml`)} /home/agent/.omp/agent/config.yml; ln -sfn ${shellQuote(`${ompRoot}/models.yml`)} /home/agent/.omp/agent/models.yml; ln -sfn ${shellQuote(`${ompRoot}/RULES.md`)} /home/agent/.omp/agent/RULES.md`;
    run("sbx", ["exec", "-u", "0", name, "sh", "-lc", command], { capture: true });
    wakeAndVerify(name);
    emit(recipeResult(name, projectRoot));
  } catch (error) {
    if (created) {
      try {
        run("sbx", ["rm", "--force", name]);
      } catch (cleanupError) {
        log(`[WARN] cleanup failed: ${cleanupError.message}`);
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

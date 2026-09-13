#!/usr/bin/env node

import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { existsSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { homedir, tmpdir } from "node:os";
import { dirname, join, relative, resolve } from "node:path";
import { pathToFileURL } from "node:url";

const defaultImage =
  "ghcr.io/teck-lab/teck-paseo/paseo-worker:omp18.0.4-bun1.4.0-dotnet10.0.300-chrome153";
const defaultOmniRouteBaseUrl = "https://omniroute.tecklab.dk/v1";
const requiredOmpFiles = ["config.yml", "models.yml", "RULES.md", "mcp.json", "lsp.json"];
const commandScripts =
  process.env.NODE_ENV === "test"
    ? {
        sbx: process.env.TECK_SBX_SCRIPT,
        git: process.env.TECK_GIT_SCRIPT,
      }
    : {};

function log(message) {
  process.stderr.write(`${message}\n`);
}

function redact(value, sensitive = []) {
  let output = String(value);
  for (const secret of sensitive) {
    if (secret) output = output.replaceAll(secret, "<redacted>");
  }
  return output;
}

function resolveCommand(command, args) {
  const script = commandScripts[command];
  return script ? { command: process.execPath, args: [script, ...args] } : { command, args };
}

function execute(command, args, options = {}) {
  const resolvedCommand = resolveCommand(command, args);
  return spawnSync(resolvedCommand.command, resolvedCommand.args, {
    cwd: options.cwd,
    encoding: options.encoding ?? "utf8",
    input: options.input,
    windowsHide: true,
    stdio: options.capture
      ? [options.input === undefined ? "ignore" : "pipe", "pipe", "pipe"]
      : [options.input === undefined ? "ignore" : "pipe", "inherit", "inherit"],
  });
}

function run(command, args, options = {}) {
  const result = execute(command, args, options);
  const sensitive = options.sensitive ?? [];
  if (result.error) {
    throw new Error(redact(`${command} could not start: ${result.error.message}`, sensitive));
  }
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

function runOptional(command, args, options = {}) {
  const result = execute(command, args, { ...options, capture: true });
  if (result.error) throw result.error;
  return result.status === 0 ? result.stdout.trim() : "";
}

export function normalizeWorkspacePath(workspacePath, platform = process.platform) {
  const absolutePath = resolve(workspacePath).replace(/[\\/]+$/, "");
  return platform === "win32" ? absolutePath.toLowerCase() : absolutePath;
}

export function sandboxName(workspacePath, platform = process.platform) {
  const hash = createHash("sha256")
    .update(normalizeWorkspacePath(workspacePath, platform))
    .digest("hex")
    .slice(0, 12);
  return `paseo-w-${hash}`;
}

export function remoteWorkspacePath(workspacePath, platform = process.platform) {
  const absolutePath = resolve(workspacePath);
  if (platform !== "win32") return absolutePath.replaceAll("\\", "/");
  const match = /^([a-z]):[\\/](.*)$/i.exec(absolutePath);
  if (!match) throw new Error(`Only drive-letter Windows paths can be mapped: ${absolutePath}`);
  const suffix = match[2].replaceAll("\\", "/");
  return `/${match[1].toLowerCase()}${suffix ? `/${suffix}` : ""}`;
}

export function resolveGitContext(workspacePath) {
  const marker = join(workspacePath, ".git");
  if (!existsSync(marker)) throw new Error(`Workspace has no .git metadata: ${workspacePath}`);
  if (!readFileIfRegular(marker)) {
    return {
      commonGitDir: marker,
      gitDir: marker,
      workTree: workspacePath,
    };
  }

  const markerContents = readFileSync(marker, "utf8").trim();
  const gitDirValue = /^gitdir:\s*(.+)$/i.exec(markerContents)?.[1]?.trim();
  if (!gitDirValue) throw new Error(`Invalid git worktree marker: ${marker}`);
  const gitDir = resolve(workspacePath, gitDirValue);
  const commonDirMarker = join(gitDir, "commondir");
  const commonGitDir = existsSync(commonDirMarker)
    ? resolve(gitDir, readFileSync(commonDirMarker, "utf8").trim())
    : gitDir;
  return { commonGitDir, gitDir, workTree: workspacePath };
}

function readFileIfRegular(path) {
  try {
    return readFileSync(path, "utf8");
  } catch (error) {
    if (["EISDIR", "EACCES", "EPERM"].includes(error?.code)) return undefined;
    throw error;
  }
}

export function parseEnvValue(contents, key) {
  const expression = new RegExp(`^\\s*${key}=`, "m");
  const line = contents.split(/\r?\n/).find((value) => expression.test(value));
  return line
    ?.replace(expression, "")
    .trim()
    .replace(/^(['"])(.*)\1$/, "$2");
}

function loadWorkspaceConfig(workspacePath) {
  const path = join(workspacePath, ".paseo", "sandbox", "config.json");
  if (!existsSync(path)) return {};
  const config = JSON.parse(readFileSync(path, "utf8"));
  if (!config || typeof config !== "object" || Array.isArray(config)) {
    throw new Error(`Invalid sandbox config: ${path}`);
  }
  return config;
}

function runtimeDescriptor(workspacePath, gitContext, config = loadWorkspaceConfig(workspacePath)) {
  const omniRouteBaseUrl =
    process.env.TECK_OMNIROUTE_BASE_URL || config.omniRouteBaseUrl || defaultOmniRouteBaseUrl;
  const parsedOmniRouteUrl = new URL(omniRouteBaseUrl);
  if (!["http:", "https:"].includes(parsedOmniRouteUrl.protocol)) {
    throw new Error(`Unsupported OmniRoute URL: ${omniRouteBaseUrl}`);
  }
  return {
    schemaVersion: 1,
    sandboxName: sandboxName(workspacePath),
    workspacePath,
    remoteWorkspacePath: remoteWorkspacePath(workspacePath),
    remoteGitDir: remoteWorkspacePath(gitContext.gitDir),
    omniRouteBaseUrl,
  };
}

function configuredApiKey() {
  if (process.env.OMNIROUTE_API_KEY?.trim()) return process.env.OMNIROUTE_API_KEY.trim();
  const candidates = [
    process.env.TECK_OMNIROUTE_ENV_FILE?.trim(),
    join(homedir(), ".config", "teck", "omniroute.env"),
  ].filter(Boolean);
  for (const path of candidates) {
    if (!existsSync(path)) continue;
    const value = parseEnvValue(readFileSync(path, "utf8"), "OMNIROUTE_API_KEY");
    if (value && !value.startsWith("change-me")) return value;
  }
  throw new Error(
    "OmniRoute key not found; configure ~/.config/teck/omniroute.env or TECK_OMNIROUTE_ENV_FILE",
  );
}

function configuredSigningKey() {
  const path =
    process.env.TECK_GPG_SIGNING_KEY_FILE?.trim() ||
    join(homedir(), ".config", "teck", "sandbox-signing-key.asc");
  if (!existsSync(path)) return undefined;
  const value = readFileSync(path, "utf8").trim();
  return value.startsWith("-----BEGIN PGP ") && value.includes("PRIVATE KEY BLOCK-----")
    ? value
    : undefined;
}

function ensureDaemon() {
  const status = execute("sbx", ["daemon", "status"], { capture: true });
  if (!status.error && status.status === 0) return;
  run("sbx", ["daemon", "start", "--detach"], { capture: true });
  for (let attempt = 0; attempt < 50; attempt += 1) {
    const current = execute("sbx", ["daemon", "status"], { capture: true });
    if (!current.error && current.status === 0) return;
    Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 200);
  }
  throw new Error("Docker Sandbox daemon did not become ready");
}

function listSandboxes() {
  const attempts = 6;
  let lastResult;
  let restartedHungDaemon = false;
  for (let attempt = 0; attempt < attempts; attempt += 1) {
    const result = execute("sbx", ["ls", "--quiet"], { capture: true });
    if (result.error) throw result.error;
    if (result.status === 0) return result.stdout;
    lastResult = result;

    const detail = String(result.stderr || result.stdout || "");
    const daemonIsHung = /remained running but did not respond/i.test(detail);
    if (daemonIsHung && !restartedHungDaemon) {
      run("sbx", ["daemon", "restart"], { capture: true });
      restartedHungDaemon = true;
    }
    const daemonIsStarting = /sandboxd|docker_kaname_sandboxd|timeout after \d+s/i.test(detail);
    if (!daemonIsStarting || attempt + 1 >= attempts) break;
    Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 500);
  }

  const detail = String(lastResult?.stderr || lastResult?.stdout || "").trim();
  throw new Error(
    `sbx ls --quiet failed with exit code ${lastResult?.status}${detail ? `: ${detail}` : ""}`,
  );
}

function sandboxExists(name) {
  return listSandboxes()
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

function acquireLock(name) {
  const root = join(tmpdir(), "teck-paseo-sandboxes");
  const directory = join(root, `${name}.lock`);
  const pidPath = join(directory, "pid");
  mkdirSync(root, { recursive: true });
  const deadline = Date.now() + Number(process.env.TECK_PASEO_LOCK_TIMEOUT_MS || 300000);
  while (true) {
    try {
      mkdirSync(directory);
      writeFileSync(pidPath, `${process.pid}\n`);
      return () => rmSync(directory, { recursive: true, force: true });
    } catch (error) {
      if (error?.code !== "EEXIST") throw error;
      const holder = existsSync(pidPath) ? Number(readFileSync(pidPath, "utf8")) : 0;
      if (!holder || !processAlive(holder)) {
        rmSync(directory, { recursive: true, force: true });
        continue;
      }
      if (Date.now() >= deadline) throw new Error(`Timed out waiting for sandbox lock ${name}`);
      Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 250);
    }
  }
}

function shellQuote(value) {
  return `'${String(value).replaceAll("'", `'"'"'`)}'`;
}

function validateWorkspace(workspacePath) {
  const packagePath = join(workspacePath, "package.json");
  if (!existsSync(packagePath)) throw new Error(`Workspace has no package.json: ${workspacePath}`);
  const packageJson = JSON.parse(readFileSync(packagePath, "utf8"));
  if (packageJson.name !== "teck-platform") {
    throw new Error(`Paseo sandbox integration only supports Teck.Monorepo: ${workspacePath}`);
  }
  const missing = requiredOmpFiles.filter((file) => !existsSync(join(workspacePath, ".omp", file)));
  if (missing.length > 0) throw new Error(`Workspace is missing OMP config: ${missing.join(", ")}`);
}

function collectFiles(root, directory = root) {
  if (!existsSync(directory)) return [];
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    return entry.isDirectory()
      ? collectFiles(root, path)
      : [{ path, relativePath: relative(root, path) }];
  });
}

function writeSandboxFile(name, destination, contents, mode = "600") {
  run(
    "sbx",
    [
      "exec",
      "-i",
      "-u",
      "agent",
      name,
      "sh",
      "-lc",
      `umask 077; cat > ${shellQuote(destination)}; chmod ${mode} ${shellQuote(destination)}`,
    ],
    { capture: true, input: contents },
  );
}

function syncOmpConfiguration(name, workspacePath, omniRouteBaseUrl) {
  const ompRoot = join(workspacePath, ".omp");
  run(
    "sbx",
    [
      "exec",
      "-u",
      "agent",
      name,
      "sh",
      "-lc",
      "set -eu; install -d -m 700 /home/agent/.omp/agent /home/agent/.omp/run; rm -rf /home/agent/.omp/agent/extensions /home/agent/.omp/agent/scripts; install -d -m 700 /home/agent/.omp/agent/extensions /home/agent/.omp/agent/scripts",
    ],
    { capture: true },
  );
  for (const file of [...requiredOmpFiles, "WATCHDOG.yml"]) {
    const source = join(ompRoot, file);
    if (existsSync(source)) {
      const contents = readFileSync(source, "utf8");
      const configuredContents =
        file === "models.yml"
          ? contents.replace(/^(\s*baseUrl:\s*).+$/m, `$1${omniRouteBaseUrl}`)
          : contents;
      writeSandboxFile(name, `/home/agent/.omp/agent/${file}`, configuredContents);
    }
  }
  for (const directory of ["extensions", "scripts"]) {
    for (const file of collectFiles(join(ompRoot, directory))) {
      const destinationDirectory = dirname(file.relativePath).replaceAll("\\", "/");
      if (destinationDirectory !== ".") {
        run(
          "sbx",
          [
            "exec",
            "-u",
            "agent",
            name,
            "install",
            "-d",
            "-m",
            "700",
            `/home/agent/.omp/agent/${directory}/${destinationDirectory}`,
          ],
          { capture: true },
        );
      }
      const mode = directory === "scripts" ? "700" : "600";
      writeSandboxFile(
        name,
        `/home/agent/.omp/agent/${directory}/${file.relativePath.replaceAll("\\", "/")}`,
        readFileSync(file.path, "utf8"),
        mode,
      );
    }
  }
}

function syncGitConfiguration(name, workspacePath) {
  const authorName = runOptional("git", ["-C", workspacePath, "config", "user.name"]);
  const authorEmail = runOptional("git", ["-C", workspacePath, "config", "user.email"]);
  if (authorName)
    run(
      "sbx",
      [
        "exec",
        "-u",
        "agent",
        name,
        "git",
        "-C",
        "/tmp",
        "config",
        "--global",
        "user.name",
        authorName,
      ],
      { capture: true },
    );
  if (authorEmail)
    run(
      "sbx",
      [
        "exec",
        "-u",
        "agent",
        name,
        "git",
        "-C",
        "/tmp",
        "config",
        "--global",
        "user.email",
        authorEmail,
      ],
      { capture: true },
    );

  const signingKey = configuredSigningKey();
  if (!signingKey) return;
  const signingHome = "/home/agent/.gnupg-paseo-signing";
  const gpgProgram = "/home/agent/.local/bin/paseo-gpg";
  const command = [
    "set -eu",
    "cd /tmp",
    `rm -rf ${signingHome}`,
    `install -d -m 700 ${signingHome} /home/agent/.local/bin`,
    `gpg --batch --homedir ${signingHome} --import`,
    `fingerprint="$(gpg --batch --homedir ${signingHome} --with-colons --list-secret-keys | awk -F: '$1 == "fpr" { print $10; exit }')"`,
    'test -n "$fingerprint"',
    `printf '%s\\n' '#!/usr/bin/env sh' 'exec gpg --homedir ${signingHome} "$@"' > ${gpgProgram}`,
    `chmod 700 ${gpgProgram}`,
    'git config --global user.signingkey "$fingerprint"',
    `git config --global gpg.program ${gpgProgram}`,
    "git config --global commit.gpgsign true",
  ].join("; ");
  run("sbx", ["exec", "-i", "-u", "agent", name, "sh", "-lc", command], {
    capture: true,
    input: signingKey,
    sensitive: [signingKey],
  });
}

function verifyRuntime(name, workspacePath, gitContext) {
  const remotePath = remoteWorkspacePath(workspacePath);
  const remoteGitDir = remoteWorkspacePath(gitContext.gitDir);
  const command = [
    "set -eu",
    "test -x /usr/local/bin/omp",
    "test -x /usr/local/bin/bun",
    "test -r /home/agent/.omp/agent/config.yml",
    "test -r /home/agent/.omp/agent/models.yml",
    "test -r /home/agent/.omp/agent/RULES.md",
    `test -d ${shellQuote(remotePath)}`,
    "/usr/local/bin/omp --version >/dev/null",
    `GIT_DIR=${shellQuote(remoteGitDir)} GIT_WORK_TREE=${shellQuote(remotePath)} git status --porcelain=v1 >/dev/null`,
  ].join("; ");
  run("sbx", ["exec", "-u", "agent", name, "sh", "-lc", command], { capture: true });
}

function ensureSandbox(workspacePath, options = {}) {
  validateWorkspace(workspacePath);
  const gitContext = resolveGitContext(workspacePath);
  ensureDaemon();
  const config = loadWorkspaceConfig(workspacePath);
  const descriptor = runtimeDescriptor(workspacePath, gitContext, config);
  const name = descriptor.sandboxName;
  const release = acquireLock(name);
  let created = false;
  try {
    if (options.recreate && sandboxExists(name)) {
      log(`[RECREATE] ${name}`);
      run("sbx", ["rm", "--force", name], { capture: true });
    }
    if (!sandboxExists(name)) {
      const image = process.env.TECK_PASEO_SANDBOX_IMAGE || config.image || defaultImage;
      const cpus = String(process.env.TECK_PASEO_SANDBOX_CPUS || config.cpus || 4);
      const memory = String(process.env.TECK_PASEO_SANDBOX_MEMORY || config.memory || "4g");
      const kitPath = join(workspacePath, ".paseo", "sandbox", "kit");
      if (!existsSync(join(kitPath, "spec.yaml")))
        throw new Error(`Sandbox kit is missing: ${kitPath}`);
      log(`[CREATE] ${name} (${cpus} CPU, ${memory}) -> ${workspacePath}`);
      const workspaceMounts = [workspacePath];
      if (
        normalizeWorkspacePath(gitContext.commonGitDir) !==
        normalizeWorkspacePath(join(workspacePath, ".git"))
      ) {
        workspaceMounts.push(gitContext.commonGitDir);
      }
      run("sbx", [
        "create",
        "--name",
        name,
        "--cpus",
        cpus,
        "--memory",
        memory,
        "--kit",
        kitPath,
        "--template",
        image,
        "shell",
        ...workspaceMounts,
      ]);
      created = true;
    }
    run("sbx", ["exec", name, "true"], { capture: true });
    const apiKey = configuredApiKey();
    const { omniRouteBaseUrl } = descriptor;
    const parsedOmniRouteUrl = new URL(omniRouteBaseUrl);
    const omniRouteHost = parsedOmniRouteUrl.hostname;
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
        apiKey,
      ],
      { capture: true, sensitive: [apiKey] },
    );
    syncOmpConfiguration(name, workspacePath, omniRouteBaseUrl);
    syncGitConfiguration(name, workspacePath);
    verifyRuntime(name, workspacePath, gitContext);
    return descriptor;
  } catch (error) {
    if (created && process.env.TECK_PASEO_KEEP_FAILED !== "1") {
      try {
        run("sbx", ["rm", "--force", name], { capture: true });
      } catch (cleanupError) {
        log(`[WARN] Could not remove failed sandbox ${name}: ${cleanupError.message}`);
      }
    }
    throw error;
  } finally {
    release();
  }
}

function attachSandbox(workspacePath) {
  validateWorkspace(workspacePath);
  const gitContext = resolveGitContext(workspacePath);
  ensureDaemon();
  const descriptor = runtimeDescriptor(workspacePath, gitContext);
  const release = acquireLock(descriptor.sandboxName);
  try {
    if (!sandboxExists(descriptor.sandboxName)) {
      throw new Error(
        `Sandbox ${descriptor.sandboxName} is missing; the Paseo worktree setup did not complete`,
      );
    }
    run("sbx", ["exec", descriptor.sandboxName, "true"], { capture: true });
    return descriptor;
  } finally {
    release();
  }
}

function destroySandbox(workspacePath) {
  ensureDaemon();
  const name = sandboxName(workspacePath);
  const release = acquireLock(name);
  try {
    if (!sandboxExists(name)) return { schemaVersion: 1, sandboxName: name, removed: false };
    log(`[DESTROY] ${name} -> ${workspacePath}`);
    run("sbx", ["rm", "--force", name], { capture: true });
    return { schemaVersion: 1, sandboxName: name, removed: true };
  } finally {
    release();
  }
}

function parseArguments(argv) {
  const [action, ...rest] = argv;
  let workspacePath = process.env.PASEO_WORKTREE_PATH || process.cwd();
  let recreate = false;
  for (let index = 0; index < rest.length; index += 1) {
    if (rest[index] === "--workspace") workspacePath = rest[++index];
    else if (rest[index] === "--recreate") recreate = true;
    else throw new Error(`Unknown argument: ${rest[index]}`);
  }
  return { action, workspacePath: resolve(workspacePath), recreate };
}

export function runLifecycle(argv = process.argv.slice(2)) {
  const { action, workspacePath, recreate } = parseArguments(argv);
  if (action === "ensure") return ensureSandbox(workspacePath, { recreate });
  if (action === "attach") return attachSandbox(workspacePath);
  if (action === "destroy") return destroySandbox(workspacePath);
  if (action === "name") {
    return {
      schemaVersion: 1,
      sandboxName: sandboxName(workspacePath),
      workspacePath,
      remoteWorkspacePath: remoteWorkspacePath(workspacePath),
    };
  }
  throw new Error(
    "Usage: lifecycle.mjs <ensure|attach|destroy|name> [--workspace PATH] [--recreate]",
  );
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try {
    process.stdout.write(`${JSON.stringify(runLifecycle())}\n`);
  } catch (error) {
    log(`[ERROR] ${error.message}`);
    process.exitCode = 1;
  }
}

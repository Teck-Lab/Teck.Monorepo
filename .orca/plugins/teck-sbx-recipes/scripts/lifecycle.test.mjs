import assert from "node:assert/strict";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import {
  bundledSkillPaths,
  csharpLsInstallCommand,
  deterministicPort,
  gitAuthorConfigArgs,
  githubMcpAuthorized,
  githubMcpRegistration,
  githubMcpRegistrationMatches,
  keepaliveTaskScript,
  nodeToolchainRepairCommand,
  parseIdentity,
  parsePublishedPort,
  recipeResult,
  reconcileKnownHost,
  requiredTeckPaths,
  sandboxName,
  sandboxState,
  scheduledTaskName,
  signingCommand,
  sshdPrerequisiteCommand,
  teckConfigPaths,
  validateTeckRepo,
  wakeCheckCommand,
} from "./lifecycle.mjs";

test("known host reconciliation replaces stale keys idempotently", (t) => {
  const home = mkdtempSync(join(tmpdir(), "teck-known-host-"));
  t.after(() => rmSync(home, { recursive: true, force: true }));
  mkdirSync(join(home, ".ssh"));
  const knownHosts = join(home, ".ssh", "known_hosts");
  writeFileSync(knownHosts, "[127.0.0.1]:38130 ssh-ed25519 stale\nother ssh-ed25519 keep\n");
  const key = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAITest root@sandbox";
  reconcileKnownHost(38130, key, home);
  reconcileKnownHost(38130, key, home);
  assert.equal(
    readFileSync(knownHosts, "utf8"),
    "other ssh-ed25519 keep\n[127.0.0.1]:38130 ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAITest\n",
  );
});

test("known host reconciliation creates a fresh SSH directory", (t) => {
  const home = mkdtempSync(join(tmpdir(), "teck-known-host-fresh-"));
  t.after(() => rmSync(home, { recursive: true, force: true }));
  reconcileKnownHost(38130, "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAITest", home);
  assert.match(
    readFileSync(join(home, ".ssh", "known_hosts"), "utf8"),
    /^\[127\.0\.0\.1\]:38130 ssh-ed25519 /,
  );
});

test("canonical OMP config suppresses onboarding", () => {
  const config = readFileSync(new URL("../../../../.omp/config.yml", import.meta.url), "utf8");
  assert.match(config, /^setupVersion: 2$/m);
  assert.match(config, /^ {2}setupWizard: false$/m);
  const kit = readFileSync(new URL("../kit/spec.yaml", import.meta.url), "utf8");
  assert.match(kit, /^ {4}OMP_SKIP_SETUP: "1"$/m);
  assert.match(kit, /^ {4}OMNIROUTE_RESEARCH_ENABLED: "1"$/m);
  assert.doesNotMatch(kit, /SEARXNG_TOKEN|CRAWL4AI_API_TOKEN/);
  assert.doesNotMatch(kit, /search\.tecklab\.dk|reader\.tecklab\.dk/);
  const mcp = JSON.parse(
    readFileSync(new URL("../../../../.omp/mcp.json", import.meta.url), "utf8"),
  );
  assert.equal(mcp.mcpServers.crawl4ai, undefined);
  assert.equal(mcp.mcpServers["next-devtools"].command, "next-devtools-mcp");
  assert.deepEqual(mcp.mcpServers["next-devtools"].args, []);
});
test("sshd prerequisites wait for Docker's startup apt job", () => {
  const command = sshdPrerequisiteCommand();
  assert.ok(command.includes("timed out waiting for sandbox apt startup job"));
  assert.ok(command.includes("DPkg::Lock::Timeout=300"));
  assert.ok(command.includes("openssh-server"));
});
test("toolchain repair reconciles stale sandbox images", () => {
  const nodeCommand = nodeToolchainRepairCommand();
  assert.ok(nodeCommand.includes("typescript-language-server@4.4.1"));
  assert.ok(nodeCommand.includes("typescript@5.9.3"));
  assert.ok(nodeCommand.includes("next-devtools-mcp@0.4.0"));
  assert.ok(nodeCommand.includes('ln -sf "$global_bin/$tool" "/usr/local/bin/$tool"'));
  const csharpCommand = csharpLsInstallCommand();
  assert.ok(csharpCommand.includes("cat > /tmp/csharp-ls.0.27.0.nupkg"));
  assert.ok(csharpCommand.includes("--add-source /tmp --no-cache"));
});

test("GitHub MCP uses the hosted GitHub App gateway registration", () => {
  assert.deepEqual(githubMcpRegistration(), {
    name: "github",
    type: "remote",
    url: "https://api.githubcopilot.com/mcp/",
  });
  assert.equal(
    githubMcpRegistrationMatches(
      "Name:      github\nType:      remote\nURL:       https://api.githubcopilot.com/mcp/\nTransport: streamable-http\n",
    ),
    true,
  );
  assert.equal(githubMcpRegistrationMatches("Name: github\nType: local\n"), false);
  assert.equal(githubMcpAuthorized('[{"server_name":"github","status":"authorized"}]'), true);
  assert.equal(githubMcpAuthorized('[{"server_name":"github","status":"unauthorized"}]'), false);
  assert.equal(githubMcpAuthorized("not-json"), false);
});

test("Teck recipe requires committed OMP configuration", () => {
  assert.deepEqual(requiredTeckPaths(), [
    ".omp/config.yml",
    ".omp/models.yml",
    ".omp/RULES.md",
    ".omp/mcp.json",
    ".omp/lsp.json",
    ".omp/scripts/github-mcp-check.mjs",
  ]);
});

test("Teck repository validation rejects missing and wrong package identity", (t) => {
  const repo = mkdtempSync(join(tmpdir(), "teck-plugin-repo-"));
  t.after(() => rmSync(repo, { recursive: true, force: true }));
  assert.throws(() => validateTeckRepo(repo), /requires package.json/);
  writeFileSync(join(repo, "package.json"), '{"name":"other"}');
  assert.throws(() => validateTeckRepo(repo), /only provision Teck.Monorepo/);
});

test("Teck repository validation rejects every missing OMP asset", () => {
  for (const omitted of requiredTeckPaths()) {
    const repo = mkdtempSync(join(tmpdir(), "teck-plugin-repo-"));
    try {
      writeFileSync(join(repo, "package.json"), '{"name":"teck-platform"}');
      mkdirSync(join(repo, ".omp", "scripts"), { recursive: true });
      for (const path of requiredTeckPaths()) {
        if (path !== omitted) writeFileSync(join(repo, path), "ok");
      }
      assert.throws(() => validateTeckRepo(repo), new RegExp(omitted.replace(".", "\\.")));
    } finally {
      rmSync(repo, { recursive: true, force: true });
    }
  }
});

test("Teck repository validation accepts the expected project", (t) => {
  const repo = mkdtempSync(join(tmpdir(), "teck-plugin-repo-"));
  t.after(() => rmSync(repo, { recursive: true, force: true }));
  writeFileSync(join(repo, "package.json"), '{"name":"teck-platform"}');
  mkdirSync(join(repo, ".omp", "scripts"), { recursive: true });
  for (const path of requiredTeckPaths()) writeFileSync(join(repo, path), "ok");
  assert.doesNotThrow(() => validateTeckRepo(repo));
});

test("sandbox identity is stable per Orca project", () => {
  assert.equal(sandboxName("project-a"), sandboxName("project-a"));
  assert.notEqual(sandboxName("project-a"), sandboxName("project-b"));
  assert.match(sandboxName("project-a"), /^orca-p-[0-9a-f]{12}$/);
});

test("host SSH state is outside the repository and per project", () => {
  const state = sandboxState("orca-p-123456789abc", "C:/Users/test");
  assert.equal(state.directory, join("C:/Users/test", ".orca-sbx", "orca-p-123456789abc"));
  assert.equal(state.identityFile, join(state.directory, "id_ed25519"));
  assert.equal(state.hostKeyFile, join(state.directory, "ssh_host_ed25519_key"));
});

test("plugin provisions the bundled orchestration skill into OMP", () => {
  const [skill] = bundledSkillPaths({ home: "/root" });
  assert.equal(skill.destination, "/root/.omp/agent/skills/orchestration/SKILL.md");
  assert.ok(skill.source.endsWith(join("skills", "orchestration", "SKILL.md")));
  assert.match(readFileSync(skill.source, "utf8"), /^name: orchestration$/m);
  assert.ok(wakeCheckCommand("/root").includes("/root/.omp/agent/skills/orchestration/SKILL.md"));
  const wake = wakeCheckCommand("/root");
  assert.ok(wake.includes("PUPPETEER_EXECUTABLE_PATH"));
  assert.ok(wake.includes("PUPPETEER_PROXY"));
  assert.ok(wake.includes("google-chrome-stable --version"));
});

test("project keepalive runs under Task Scheduler and restarts at logon", () => {
  assert.equal(
    scheduledTaskName("orca-p-123456789abc"),
    "Teck Docker Sandbox Keepalive orca-p-123456789abc",
  );
  const script = keepaliveTaskScript("orca-p-123456789abc");
  assert.ok(script.includes("New-ScheduledTaskTrigger -AtLogOn"));
  assert.ok(script.includes("-WindowStyle Hidden"));
  assert.ok(script.includes("-Execute 'powershell.exe'"));
  assert.ok(script.includes("exec -u 0 '''+$name+''' sh -lc"));
  assert.ok(script.includes("install -d -m 755 /run/sshd"));
  assert.ok(script.includes("pgrep -x sshd"));
  assert.ok(script.includes("exec sleep infinity"));
  assert.ok(script.includes("Unregister-ScheduledTask"));
  assert.ok(script.includes("Start-ScheduledTask -TaskName $task"));
});

test("legacy visible keepalive tasks are replaced", () => {
  const script = keepaliveTaskScript("orca-p-123456789abc");
  assert.ok(script.includes("$current.Actions.Execute -ne 'powershell.exe'"));
  assert.ok(script.includes("$current.Actions.Arguments -notmatch 'WindowStyle Hidden'"));
  assert.ok(script.includes("$current.Actions.Arguments -notmatch 'pgrep -x sshd'"));
});

test("published SSH mapping accepts only IPv4 loopback port 2222", () => {
  const json = JSON.stringify([
    { host_ip: "::1", host_port: 31234, sandbox_port: 2222, protocol: "tcp" },
    { host_ip: "127.0.0.1", host_port: 31234, sandbox_port: 2222, protocol: "tcp" },
  ]);
  assert.equal(parsePublishedPort(json), 31234);
  assert.throws(() => parsePublishedPort("{}"), /invalid port JSON/);
  assert.throws(
    () =>
      parsePublishedPort(
        '[{"host_ip":"0.0.0.0","host_port":22,"sandbox_port":2222,"protocol":"tcp"}]',
      ),
    /did not publish/,
  );
});

test("recipe emits direct TCP SSH with persistent identity", () => {
  const result = recipeResult("orca-p-123456789abc", "/root/project", {
    port: 31234,
    username: "root",
    identityFile: "C:/Users/test/.orca-sbx/orca-p-123456789abc/id_ed25519",
  });
  assert.equal(result.connection.target.host, "127.0.0.1");
  assert.equal(result.connection.target.port, 31234);
  assert.equal(result.connection.target.username, "root");
  assert.equal(result.connection.target.identitiesOnly, true);
  assert.equal(result.connection.target.relayGracePeriodSeconds, 86400);
  assert.equal(result.connection.projectRoot, "/root/project");
});

test("identity and Teck checks use the effective home", () => {
  assert.deepEqual(parseIdentity("root\r\n", "/root\r\n"), { username: "root", home: "/root" });
  const wake = wakeCheckCommand("/root");
  assert.ok(wake.includes("/root/.omp/agent/models.yml"));
  assert.ok(wake.includes("/root/.local/bin/orca-gpg"));
  assert.ok(wake.includes("/usr/local/bin/orca-runtime-check"));
  assert.ok(wake.includes("/root/.omp/agent/mcp.json"));
  assert.ok(wake.includes("OMNIROUTE_RESEARCH_ENABLED"));
  assert.ok(wake.includes("next-devtools-mcp --help"));
  assert.ok(wake.includes("MCP_GATEWAY_URL"));
  assert.ok(wake.includes("github-mcp-check.mjs"));
  const signing = signingCommand({ username: "root", home: "/root" });
  assert.ok(signing.includes("/root/.gnupg-orca-signing"));
  assert.ok(!signing.includes("/home/agent"));
});

test("Teck provisioning reads cloned config and sets sandbox author", () => {
  const identity = { username: "root", home: "/root" };
  assert.deepEqual(teckConfigPaths(identity, "/root/project"), [
    { source: "/root/project/.omp/config.yml", destination: "/root/.omp/agent/config.yml" },
    { source: "/root/project/.omp/models.yml", destination: "/root/.omp/agent/models.yml" },
    { source: "/root/project/.omp/RULES.md", destination: "/root/.omp/agent/RULES.md" },
    { source: "/root/project/.omp/mcp.json", destination: "/root/.omp/agent/mcp.json" },
    { source: "/root/project/.omp/lsp.json", destination: "/root/.omp/agent/lsp.json" },
  ]);
  assert.deepEqual(gitAuthorConfigArgs("orca-p-123456789abc", identity, "user.name", "Test User"), [
    "exec",
    "-u",
    "root",
    "orca-p-123456789abc",
    "git",
    "config",
    "--global",
    "user.name",
    "Test User",
  ]);
  assert.throws(
    () => gitAuthorConfigArgs("orca-p-123456789abc", identity, "user.email", ""),
    /Host Git user.email is missing/,
  );
});

test("deterministic port remains in the private recipe range", () => {
  const port = deterministicPort("orca-p-123456789abc");
  assert.ok(port >= 30000 && port <= 39999);
});

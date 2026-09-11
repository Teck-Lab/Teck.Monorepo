import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { copyFileSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { delimiter, join, resolve } from "node:path";
import test from "node:test";

const source = resolve(import.meta.dirname, "lifecycle.mjs");

function fixture(t) {
  const root = mkdtempSync(join(tmpdir(), "teck-lifecycle-flow-"));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  const home = join(root, "home");
  const repo = join(root, "repo");
  const bin = join(root, "bin");
  mkdirSync(join(home, ".config", "teck"), { recursive: true });
  mkdirSync(join(repo, ".omp"), { recursive: true });
  mkdirSync(bin, { recursive: true });
  writeFileSync(join(repo, "package.json"), '{"name":"teck-platform"}');
  for (const file of ["config.yml", "models.yml", "RULES.md", "mcp.json", "lsp.json"]) {
    writeFileSync(join(repo, ".omp", file), `${file}\n`);
  }
  mkdirSync(join(repo, ".omp", "scripts"), { recursive: true });
  writeFileSync(join(repo, ".omp", "scripts", "github-mcp-check.mjs"), "probe\n");
  writeFileSync(join(home, ".config", "teck", "omniroute.env"), "OMNIROUTE_API_KEY=test-key\n");
  writeFileSync(
    join(home, ".config", "teck", "sandbox-signing-key.asc"),
    [
      "-----BEGIN PGP ",
      "PRIVATE KEY BLOCK-----\ntest\n-----END PGP ",
      "PRIVATE KEY BLOCK-----\n",
    ].join(""),
  );
  const projectId = "flow-project";
  const name = `orca-p-${createHash("sha256").update(projectId).digest("hex").slice(0, 12)}`;
  const statePath = join(root, "state.json");
  writeFileSync(
    statePath,
    JSON.stringify({
      exists: false,
      creates: 0,
      clones: 0,
      worktrees: 1,
      removes: 0,
      gatewayReady: false,
      calls: [],
    }),
  );
  writeFileSync(
    join(bin, "sbx-stub.mjs"),
    `import{readFileSync,writeFileSync}from'node:fs';const a=process.argv.slice(2),p=process.env.STUB_STATE,s=JSON.parse(readFileSync(p));s.calls.push(a.join(' '));const save=()=>writeFileSync(p,JSON.stringify(s));process.on('exit',save);if(a[0]==='version')console.log('v0.42.1');else if(a[0]==='ls'){if(s.exists)console.log(process.env.STUB_NAME)}else if(a[0]==='create'){s.exists=true;s.creates++;if(a.includes('--static-mcp'))s.gatewayReady=true}else if(a[0]==='mcp'&&a[1]==='inspect')console.log('Name:      github\\nType:      remote\\nURL:       https://api.githubcopilot.com/mcp/\\nTransport: streamable-http');else if(a[0]==='mcp'&&a[1]==='auth')console.log('[{"server_name":"github","status":"authorized"}]');else if(a[0]==='mcp'&&a[1]==='load'){s.gatewayReady=true}else if(a[0]==='ports'&&a.includes('--json'))console.log(JSON.stringify([{host_ip:'127.0.0.1',host_port:38130,sandbox_port:2222,protocol:'tcp'}]));else if(a[0]==='ports'){}else if(a[0]==='secret'){}else if(a[0]==='rm'){s.exists=false;s.removes++}else if(a[0]==='exec'){const c=a.join(' ');if(c.includes(' id -un'))console.log('root');else if(c.includes('printf "%s" "$HOME"'))console.log('/root');else if(c.includes('test -d /root/project/.git'))process.exit(s.clones?0:1);else if(c.includes(' git clone '))s.clones++;else if(c.includes('github-mcp-check.mjs')&&!s.gatewayReady)process.exit(1);else if(c.includes('cat /etc/ssh/ssh_host_ed25519_key.pub'))console.log('ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAITest root@sandbox');else if(c.includes('cat /etc/ssh/ssh_host_ed25519_key'))console.log('host-private');else if(c.includes('worktree list --porcelain')){for(let i=0;i<s.worktrees;i++)console.log('worktree /root/project'+i)}}`,
  );
  writeFileSync(
    join(bin, "git-stub.mjs"),
    `const a=process.argv.slice(2).join(' ');if(a.includes('remote get-url'))console.log('https://example.invalid/repo.git');else if(a.includes('user.name'))console.log('Test User');else if(a.includes('user.email'))console.log('test@example.com');`,
  );
  writeFileSync(
    join(bin, "ssh-keygen-stub.mjs"),
    `import{writeFileSync}from'node:fs';const a=process.argv.slice(2);if(a.includes('-f')&&a.includes('-t')){const f=a[a.indexOf('-f')+1];writeFileSync(f,'client-private');writeFileSync(f+'.pub','ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIClient')}else if(a.includes('-R'))process.exit(0);`,
  );
  const installed = join(root, "installed");
  mkdirSync(join(installed, "scripts"), { recursive: true });
  mkdirSync(join(installed, "kit"));
  mkdirSync(join(installed, "skills", "orchestration"), { recursive: true });
  copyFileSync(source, join(installed, "scripts", "lifecycle.mjs"));
  writeFileSync(
    join(installed, "kit", "spec.yaml"),
    "schemaVersion: '2'\nkind: mixin\nname: test\n",
  );
  writeFileSync(
    join(installed, "skills", "orchestration", "SKILL.md"),
    "---\nname: orchestration\ndescription: Test orchestration skill.\n---\n",
  );
  return {
    home,
    statePath,
    name,
    lifecycle: join(installed, "scripts", "lifecycle.mjs"),
    env: {
      ...process.env,
      PATH: `${bin}${delimiter}${process.env.PATH}`,
      USERPROFILE: home,
      HOME: home,
      ORCA_REPO_PATH: repo,
      ORCA_REPO_URL: "https://example.invalid/repo.git",
      NODE_ENV: "test",
      ORCA_SBX_SCRIPT: join(bin, "sbx-stub.mjs"),
      ORCA_GIT_SCRIPT: join(bin, "git-stub.mjs"),
      ORCA_SSH_KEYGEN_SCRIPT: join(bin, "ssh-keygen-stub.mjs"),
      ORCA_PROJECT_ID: projectId,
      ORCA_GPG_SIGNING_KEY_FILE: join(home, ".config", "teck", "sandbox-signing-key.asc"),
      STUB_STATE: statePath,
      STUB_NAME: name,
    },
  };
}

function runAction(context, action, payload = "") {
  return spawnSync(process.execPath, [context.lifecycle, action], {
    encoding: "utf8",
    env: context.env,
    input: payload,
  });
}

function readState(context) {
  return JSON.parse(readFileSync(context.statePath, "utf8"));
}

function writeState(context, state) {
  writeFileSync(context.statePath, JSON.stringify(state));
}
test("actual create action creates once and reuses the same project sandbox", (t) => {
  const context = fixture(t);
  const first = runAction(context, "create");
  assert.equal(first.status, 0, first.stderr);
  const result = JSON.parse(first.stdout);
  assert.equal(result.connection.projectRoot, "/root/project");
  assert.equal(result.connection.target.host, "127.0.0.1");
  assert.equal(result.connection.target.port, 38130);
  assert.equal(result.connection.target.username, "root");
  assert.equal(result.connection.target.identitiesOnly, true);
  const second = runAction(context, "create");
  assert.equal(second.status, 0, second.stderr);
  const current = readState(context);
  assert.equal(current.creates, 1);
  assert.equal(current.clones, 1);
  assert.ok(current.calls.some((call) => call.includes("sshd -E /tmp/orca-sshd.log -p 2222")));
  assert.ok(
    current.calls.some((call) => call.includes("create") && call.includes("--static-mcp github")),
  );
  assert.ok(current.calls.some((call) => call.startsWith("ports ")));
  assert.ok(
    current.calls.some(
      (call) => call.includes("secret set-custom") && call.includes("omniroute.tecklab.dk"),
    ),
  );
  assert.ok(current.calls.some((call) => call.includes("typescript-language-server@4.4.1")));
  assert.ok(current.calls.some((call) => call.includes("mcp load github")));
  assert.ok(current.calls.some((call) => call.includes("github-mcp-check.mjs")));

  assert.ok(current.calls.some((call) => call.includes("skills/orchestration/SKILL.md")));
});
test("actual suspend and resume preserve and repair the shared sandbox", (t) => {
  const context = fixture(t);
  const created = runAction(context, "create");
  assert.equal(created.status, 0, created.stderr);
  const payload = JSON.stringify({ recipeResult: JSON.parse(created.stdout) });
  assert.equal(runAction(context, "suspend", payload).status, 0);
  const beforeResume = readState(context).calls.length;
  const resumed = runAction(context, "resume", payload);
  assert.equal(resumed.status, 0, resumed.stderr);
  assert.equal(JSON.parse(resumed.stdout).connection.projectRoot, "/root/project");
  const resumeCalls = readState(context).calls.slice(beforeResume);
  assert.ok(resumeCalls.some((call) => call.includes("exec orca-p-") && call.endsWith(" true")));
  assert.ok(resumeCalls.some((call) => call.includes("mcp load github")));
  assert.ok(resumeCalls.some((call) => call.includes("github-mcp-check.mjs")));
  assert.ok(resumeCalls.some((call) => call.includes("sshd -E /tmp/orca-sshd.log -p 2222")));
  assert.ok(resumeCalls.some((call) => call.includes("typescript-language-server@4.4.1")));
  assert.equal(readState(context).exists, true);
});
test("lifecycle payload cannot target another project sandbox", (t) => {
  const context = fixture(t);
  const payload = JSON.stringify({
    userData: {
      provider: "teck-docker-sandbox",
      resourceId: "orca-p-000000000000",
    },
  });
  const resumed = runAction(context, "resume", payload);
  assert.equal(resumed.status, 1);
  assert.match(resumed.stderr, /does not belong to the current Orca project/);
  assert.equal(readState(context).calls.length, 0);
});

test("actual destroy keeps siblings and removes the final project sandbox", (t) => {
  const context = fixture(t);
  const created = runAction(context, "create");
  assert.equal(created.status, 0, created.stderr);
  const payload = JSON.stringify({ recipeResult: JSON.parse(created.stdout) });
  const shared = readState(context);
  shared.worktrees = 2;
  writeState(context, shared);
  assert.equal(runAction(context, "destroy", payload).status, 0);
  assert.equal(readState(context).exists, true);
  assert.equal(readState(context).removes, 0);
  const final = readState(context);
  final.worktrees = 1;
  writeState(context, final);
  assert.equal(runAction(context, "destroy", payload).status, 0);
  const removed = readState(context);
  assert.equal(removed.exists, false);
  assert.equal(removed.removes, 1);
  assert.ok(removed.calls.some((call) => call.includes("worktree prune")));
  assert.ok(removed.calls.some((call) => call.includes("secret rm")));
});

#!/usr/bin/env node

import { writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const pluginKey = "teck-lab.teck-sbx-recipes";
const command = (action) => {
  const script = [
    "$ProgressPreference='SilentlyContinue'",
    "$InformationPreference='SilentlyContinue'",
    "$PSDefaultParameterValues['*:ProgressAction']='SilentlyContinue'",
    "$ErrorActionPreference='Stop'",
    "if([string]::IsNullOrWhiteSpace($env:APPDATA)){throw 'APPDATA is unavailable'}",
    `$k='${pluginKey}'`,
    "$b=[IO.Path]::GetFullPath((Join-Path $env:APPDATA 'Orca\\plugins'))",
    "$l=Get-Content -Raw (Join-Path $b 'plugins.lock.json')|ConvertFrom-Json",
    "$h=$l.plugins.$k.contentHash",
    "if($h -notmatch '^[0-9a-f]{64}$'){throw 'Installed Teck plugin hash is invalid'}",
    "$d=[IO.Path]::GetFullPath((Join-Path $b (Join-Path $k $h)))",
    "$m=Get-Content -Raw (Join-Path $d 'orca-plugin.json')|ConvertFrom-Json",
    "if($m.publisher -ne 'teck-lab' -or $m.id -ne 'teck-sbx-recipes'){throw 'Installed Teck plugin manifest is invalid'}",
    "$x=[IO.Path]::GetFullPath((Join-Path $d 'scripts\\lifecycle.mjs'))",
    "if(-not $x.StartsWith($d+[IO.Path]::DirectorySeparatorChar)-or -not(Test-Path -LiteralPath $x)){throw 'Installed Teck plugin lifecycle is missing'}",
    `$args=@($x,'${action}')`,
    "& node @args",
    "$exitCode=$LASTEXITCODE",
    "if($null -eq $exitCode){exit 1}else{exit $exitCode}",
  ].join(";");
  const encoded = Buffer.from(script, "utf16le").toString("base64");
  return `powershell.exe -NoProfile -NonInteractive -EncodedCommand ${encoded}`;
};
const recipe = {
  schemaVersion: 1,
  id: "teck-sbx-project-sandbox",
  name: "Teck Docker Sandbox (project-shared)",
  description:
    "One Docker Sandbox microVM per Orca project, with Windows lifecycle support and Teck OMP provisioning.",
  create: command("create"),
  suspend: command("suspend"),
  resume: command("resume"),
  destroy: command("destroy"),
};
for (const [action, value] of Object.entries(recipe)) {
  if (typeof value === "string" && value.length > 7000) {
    throw new Error(`${action} exceeds the Windows command length limit`);
  }
}
writeFileSync(
  resolve(root, "recipes", "teck-sbx-project-sandbox.json"),
  `${JSON.stringify(recipe, null, 2)}\n`,
);

#requires -Version 5
param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('create', 'suspend', 'resume', 'destroy')]
  [string]$Lifecycle
)

$ErrorActionPreference = 'Stop'

# Orca can launch this file with a \\wsl.localhost\... working directory. PowerShell
# supports that cwd; cmd.exe does not. Preserve Orca's existing WSLENV forwarding
# and add the repository path translation needed by the Linux lifecycle scripts.
$wslEnvEntries = @(
  'ORCA_REPO_PATH/up',
  'ORCA_RECIPE_RESULT_SCHEMA_VERSION',
  'ORCA_VM_MODE',
  'ORCA_RECIPE_ID',
  'ORCA_VM_RECIPE_ID',
  'ORCA_VM_INSTANCE_ID',
  'ORCA_PROJECT_ID',
  'ORCA_WORKSPACE_ID',
  'ORCA_WORKSPACE_NAME',
  'ORCA_REPO_URL',
  'ORCA_REPO_REF',
  'ORCA_REPO_REF_HEAD',
  'ORCA_REPO_BRANCH',
  'ORCA_ENVIRONMENT_REF',
  'ORCA_SSH_PUBLIC_KEY',
  'ORCA_SSH_KEY_FILE/up',
  'ORCA_CODEX_AUTH_FILE/up',
  'ORCA_WINDOWS_PROFILE/up',
  'ORCA_WINDOWS_SSH_COMMAND/up',
  'ORCA_VERSION'
)
if ($env:WSLENV) {
  $wslEnvEntries += $env:WSLENV.Split(':', [System.StringSplitOptions]::RemoveEmptyEntries)
}
$env:WSLENV = ($wslEnvEntries | Select-Object -Unique) -join ':'

$linuxScript = '$ORCA_REPO_PATH/scripts/orca-vm/local-' + $Lifecycle + '.sh'
$payload = [Console]::In.ReadToEnd()
if ($payload.Length -gt 0) {
  $payload | & wsl.exe --exec bash -lc "exec `"$linuxScript`""
} else {
  & wsl.exe --exec bash -lc "exec `"$linuxScript`""
}
exit $LASTEXITCODE

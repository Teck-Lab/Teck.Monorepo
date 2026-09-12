#requires -Version 5.1

$ErrorActionPreference = 'Stop'
$root = Join-Path ([System.IO.Path]::GetTempPath()) ("teck-github-mcp-" + [guid]::NewGuid())
$bin = Join-Path $root 'bin'
$log = Join-Path $root 'sbx.log'
New-Item -ItemType Directory -Path $bin | Out-Null

try {
    @'
param([Parameter(ValueFromRemainingArguments = $true)][string[]] $Arguments)
Add-Content -Path $env:STUB_LOG -Value ($Arguments -join ' ')
if ($Arguments[0] -eq 'secret' -and $Arguments[1] -eq 'ls') {
    Write-Output '(global)          service    mcp:github.client_secret            (stored)'
    exit 0
}
if ($Arguments[0] -eq 'mcp' -and $Arguments[1] -eq 'inspect') {
    Write-Output 'Name:      github'
    Write-Output 'Type:      remote'
    Write-Output 'URL:       https://api.githubcopilot.com/mcp/'
    exit 0
}
if ($Arguments[0] -eq 'mcp' -and $Arguments[1] -eq 'auth' -and $Arguments[2] -eq 'status') {
    Write-Output '[{"server_name":"github","status":"authorized"}]'
    exit 0
}
exit 0
'@ | Set-Content -Path (Join-Path $bin 'sbx.ps1')
    Set-Content -Path (Join-Path $bin 'sbx.cmd') -Value '@powershell.exe -NoProfile -File "%~dp0sbx.ps1" %*'
    $env:STUB_LOG = $log
    $env:PATH = "$bin;$env:PATH"

    & (Join-Path $PSScriptRoot 'setup-github-mcp.ps1') -ClientId 'IvTestPublicClient'
    if ($LASTEXITCODE -ne 0) { throw 'setup failed' }

    $calls = Get-Content -Path $log
    if (-not ($calls -contains 'mcp add github --url https://api.githubcopilot.com/mcp/ --client-id IvTestPublicClient --scope offline_access --skip-auth')) {
        throw 'setup did not persist offline_access as the registration default'
    }
    if ($calls -match '^mcp auth github') {
        throw 'authorized setup unexpectedly started OAuth'
    }
    Write-Output 'setup-github-mcp persistence test passed'
}
finally {
    Remove-Item -Path $root -Recurse -Force -ErrorAction SilentlyContinue
}

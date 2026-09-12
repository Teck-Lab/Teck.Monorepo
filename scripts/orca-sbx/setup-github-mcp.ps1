#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^Iv[0-9A-Za-z]+$')]
    [string] $ClientId
)

$ErrorActionPreference = 'Stop'
$serverName = 'github'
$clientIdValue = $ClientId
$endpoint = 'https://api.githubcopilot.com/mcp/'

if (-not (Get-Command sbx -ErrorAction SilentlyContinue)) {
    throw 'sbx is required'
}

$clientSecret = (& sbx secret ls) -join "`n"
if ($LASTEXITCODE -ne 0 -or
    $clientSecret -notmatch '(?m)^\(global\)\s+service\s+mcp:github\.client_secret\s+\(stored\)$') {
    throw "GitHub App client secret is missing. Run 'sbx secret set mcp:github.client_secret' first."
}

$inspection = (& sbx mcp inspect $serverName 2>$null) -join "`n"
if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($inspection)) {
    & sbx mcp rm $serverName | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker Sandboxes could not update the hosted GitHub MCP registration.'
    }
}

Write-Host '[CONFIGURE] Registering GitHub hosted MCP with durable offline access'
& sbx mcp add $serverName --url $endpoint --client-id $clientIdValue --scope offline_access --skip-auth
if ($LASTEXITCODE -ne 0) {
    throw 'Docker Sandboxes could not register GitHub hosted MCP.'
}

$status = (& sbx mcp auth status $serverName --format json) -join ''
if ($LASTEXITCODE -ne 0) {
    throw 'Docker Sandboxes could not read GitHub MCP authorization status.'
}
$authorization = $status | ConvertFrom-Json
if ($authorization[0].status -ne 'authorized') {
    Write-Host '[AUTHORIZE] Opening one-time GitHub App authorization'
    & sbx mcp auth $serverName --scope offline_access --verbose
    if ($LASTEXITCODE -ne 0) {
        throw 'GitHub App authorization failed.'
    }
}

$status = (& sbx mcp auth status $serverName --format json) -join ''
$authorization = $status | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $authorization[0].status -ne 'authorized') {
    throw 'GitHub hosted MCP remains unauthorized.'
}

Write-Host '[PASS] Hosted GitHub MCP gateway is configured and authorized' -ForegroundColor Green
Write-Host 'New Teck sandboxes attach github automatically.'

#requires -Version 5.1
[CmdletBinding()]
param(
    [string] $SearXngTokenRef,
    [string] $Crawl4AiTokenRef
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'host-secret.ps1')

$credentialFile = Join-Path $env:USERPROFILE '.config\teck\web-services.env'

function Read-ServiceToken {
    param(
        [Parameter(Mandatory = $true)] [string] $Label,
        [string] $Reference
    )

    if (-not [string]::IsNullOrWhiteSpace($Reference)) {
        if (-not $Reference.StartsWith('op://', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unsupported $Label secret reference '$Reference'. Only op:// references are supported."
        }
        if (-not (Get-Command op -ErrorAction SilentlyContinue)) {
            throw "The 1Password CLI (op) is required to resolve the $Label secret."
        }
        $value = (& op read --no-newline $Reference) -join ''
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($value)) {
            throw "The 1Password CLI failed to read the $Label secret."
        }
        return $value
    }

    $secure = Read-Host "$Label bearer token" -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try {
        $value = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        if ([string]::IsNullOrWhiteSpace($value)) {
            throw "$Label bearer token cannot be empty."
        }
        return $value
    } finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

$searxng = $null
$crawl4ai = $null
try {
    $searxng = Read-ServiceToken -Label 'SearXNG' -Reference $SearXngTokenRef
    $crawl4ai = Read-ServiceToken -Label 'Crawl4AI' -Reference $Crawl4AiTokenRef
    $content = "SEARXNG_TOKEN=$searxng" + [Environment]::NewLine +
        "CRAWL4AI_MCP_TOKEN=$crawl4ai" + [Environment]::NewLine
    Set-TeckHostSecretFile -Path $credentialFile -Content $content
    Write-Host "[PASS] Sandbox web-service credentials configured at $credentialFile" -ForegroundColor Green
} finally {
    $searxng = $null
    $crawl4ai = $null
}

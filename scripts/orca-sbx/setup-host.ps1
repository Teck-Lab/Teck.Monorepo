#requires -Version 5.1
[CmdletBinding(DefaultParameterSetName = 'Prompt')]
param(
    [Parameter(ParameterSetName = 'Reference', Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $SecretRef
)

$ErrorActionPreference = 'Stop'
$modelsUrl = 'https://omniroute.tecklab.dk/v1/models'
$credentialDir = Join-Path $env:USERPROFILE '.config\teck'
$credentialFile = Join-Path $credentialDir 'omniroute.env'

Write-Host "[CHECK] OmniRoute host credential target: $credentialFile"

$secureKey = $null
$keyPointer = [IntPtr]::Zero
$key = $null

try {
    if ($PSCmdlet.ParameterSetName -eq 'Reference') {
        if (-not $SecretRef.StartsWith('op://', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unsupported secret reference '$SecretRef'. setup-host.ps1 resolves 1Password op:// references with the 1Password CLI (op)."
        }
        if (-not (Get-Command op -ErrorAction SilentlyContinue)) {
            throw 'The 1Password CLI (op) is required to resolve -SecretRef; install it or run setup-host.ps1 interactively.'
        }
        Write-Host "[CONFIGURE] Resolving $SecretRef"
        $key = (& op read --no-newline $SecretRef) -join ''
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($key)) {
            throw 'The 1Password CLI failed to read the secret reference.'
        }
    } else {
        Write-Host "[CHECK] OmniRoute reachable at $modelsUrl"
        try {
            Invoke-WebRequest -UseBasicParsing -Uri $modelsUrl -Method Head -TimeoutSec 5 | Out-Null
        } catch {
            # An authentication response still proves that the public endpoint
            # is reachable. Transport failures have no HTTP response.
            if (-not $_.Exception.Response) {
                throw "OmniRoute is not reachable at $modelsUrl"
            }
        }

        $secureKey = Read-Host 'OmniRoute API key' -AsSecureString
        $keyPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureKey)
        $key = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($keyPointer)
        if ([string]::IsNullOrWhiteSpace($key)) {
            throw 'The OmniRoute API key cannot be empty.'
        }
    }

    Write-Host '[VERIFY] Testing OmniRoute API key'
    try {
        Invoke-RestMethod -Uri $modelsUrl -Headers @{ Authorization = "Bearer $key" } -TimeoutSec 15 | Out-Null
    } catch {
        throw 'OmniRoute rejected the supplied API key.'
    }

    Write-Host '[CONFIGURE] Writing host-only credential'
    New-Item -ItemType Directory -Path $credentialDir -Force | Out-Null
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($credentialFile, "OMNIROUTE_API_KEY=$key" + [Environment]::NewLine, $utf8NoBom)

    # The credential is host-only: drop inherited ACLs and grant the current
    # user read access so no other local account can read the key.
    $account = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & icacls $credentialFile /inheritance:r /grant:r "$account`:(R)" 2>&1 | Out-Null
    } finally {
        $ErrorActionPreference = $previousPreference
    }
    if ($LASTEXITCODE -ne 0) {
        throw 'Failed to restrict the host credential file ACLs.'
    }

    Write-Host '[PASS] Host OmniRoute credential configured' -ForegroundColor Green
    Write-Host 'Sandbox lifecycle reads this file on every create. Re-run this script after rotating the key, then recreate existing sandboxes.'
} finally {
    if ($keyPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($keyPointer)
    }
    $key = $null
    $secureKey = $null
}

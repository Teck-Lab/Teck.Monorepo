#requires -Version 5.1
[CmdletBinding(DefaultParameterSetName = 'Prompt')]
param(
    [Parameter(ParameterSetName = 'Reference', Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $SecretRef
)

$ErrorActionPreference = 'Stop'
$placeholder = 'proxy-managed'
$modelsUrl = 'http://127.0.0.1:20128/v1/models'

if (-not (Get-Command sbx -ErrorAction SilentlyContinue)) {
    throw 'Docker Sandboxes CLI (sbx) is not available.'
}

Write-Host '[CHECK] Docker Sandboxes CLI available' -ForegroundColor Green

$secureKey = $null
$keyPointer = [IntPtr]::Zero
$key = $null
$setArguments = $null

try {
    if ($PSCmdlet.ParameterSetName -eq 'Reference') {
        Write-Host '[CONFIGURE] Global dynamic OmniRoute credential'
        $setArguments = @(
            'secret', 'set-custom',
            '--host', 'host.docker.internal',
            '--host', 'localhost',
            '--env', 'OMNIROUTE_API_KEY',
            '--placeholder', $placeholder,
            '--ref', $SecretRef,
            '--refresh', 'on-demand'
        )
    } else {
        Write-Host "[CHECK] OmniRoute reachable at $modelsUrl"
        try {
            Invoke-WebRequest -UseBasicParsing -Uri $modelsUrl -Method Head -TimeoutSec 5 | Out-Null
        } catch {
            # An authentication response still proves that the local endpoint
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

        Write-Host '[VERIFY] Testing OmniRoute API key'
        try {
            Invoke-RestMethod -Uri $modelsUrl -Headers @{ Authorization = "Bearer $key" } -TimeoutSec 15 | Out-Null
        } catch {
            throw 'OmniRoute rejected the supplied API key.'
        }

        Write-Host '[CONFIGURE] Global OmniRoute credential'
        $setArguments = @(
            'secret', 'set-custom',
            '--host', 'host.docker.internal',
            '--host', 'localhost',
            '--env', 'OMNIROUTE_API_KEY',
            '--placeholder', $placeholder,
            '--value', $key
        )
    }

    # Older sbx versions reject duplicate custom placeholders instead of
    # updating them, so rotation is an explicit global replace.
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & sbx secret rm --placeholder $placeholder --force 2>&1 | Out-Null
    } finally {
        $ErrorActionPreference = $previousPreference
    }

    & sbx @setArguments
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker Sandboxes failed to store the global OmniRoute credential.'
    }

    Write-Host '[PASS] Global OmniRoute credential configured' -ForegroundColor Green
    Write-Host 'New Docker Sandboxes can now use OmniRoute. Recreate existing sandboxes after changing a static key.'
} finally {
    if ($keyPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($keyPointer)
    }
    $key = $null
    $secureKey = $null
    $setArguments = $null
}

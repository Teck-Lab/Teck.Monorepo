#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string] $Identity = 'Teck Orca Sandbox <jl@tecklab.dk>',

    [ValidateNotNullOrEmpty()]
    [string] $KeyFile = (Join-Path $env:USERPROFILE '.config\teck\sandbox-signing-key.asc'),

    [switch] $Rotate,
    [switch] $SkipGitHubRegistration
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'host-secret.ps1')

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Program,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string[]] $Arguments
    )

    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Program failed with exit code $LASTEXITCODE."
    }
}

function Get-FirstFingerprint {
    param(
        [Parameter(Mandatory = $true)]
        [string] $GpgProgram,

        [Parameter(Mandatory = $true)]
        [string] $GpgHome
    )

    $listing = & $GpgProgram --batch --homedir $GpgHome --with-colons --list-secret-keys
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect the sandbox signing key.'
    }
    $fingerprintLine = $listing | Where-Object { $_.StartsWith('fpr:') } | Select-Object -First 1
    if (-not $fingerprintLine) {
        throw 'The sandbox signing key has no fingerprint.'
    }
    return $fingerprintLine.Split(':')[9]
}

$git = (Get-Command git -ErrorAction Stop).Source
$configuredGpg = (& $git config --global gpg.program) -join ''
$gpg = if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($configuredGpg)) {
    $configuredGpg
} else {
    (Get-Command gpg -ErrorAction Stop).Source
}

Write-Host '[CHECK] Verifying Windows GPG commit signing'
$windowsSigningKey = ((& $git config --global user.signingkey) -join '').Trim()
$windowsSigningEnabled = ((& $git config --global --bool commit.gpgsign) -join '').Trim()
if ([string]::IsNullOrWhiteSpace($windowsSigningKey) -or $windowsSigningEnabled -ne 'true') {
    throw 'Windows Git must define user.signingkey and commit.gpgsign=true before sandbox signing is configured.'
}

$checkPayload = Join-Path ([System.IO.Path]::GetTempPath()) ('orca-windows-signing-' + [guid]::NewGuid().ToString('N'))
$checkSignature = $checkPayload + '.sig'
try {
    [System.IO.File]::WriteAllText($checkPayload, 'teck-windows-gpg-signing-check')
    Invoke-Checked -Program $gpg -Arguments @('--batch', '--yes', '--local-user', $windowsSigningKey, '--output', $checkSignature, '--detach-sign', $checkPayload)
} finally {
    Remove-Item -LiteralPath $checkPayload, $checkSignature -Force -ErrorAction SilentlyContinue
}

$publicKeyFile = $KeyFile + '.pub'
if ($Rotate) {
    Remove-Item -LiteralPath $KeyFile, $publicKeyFile -Force -ErrorAction SilentlyContinue
}

$tempHome = Join-Path ([System.IO.Path]::GetTempPath()) ('orca-sandbox-signing-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempHome | Out-Null
try {
    if (Test-Path -LiteralPath $KeyFile) {
        Write-Host "[CHECK] Reusing dedicated sandbox signing key from $KeyFile"
        Invoke-Checked -Program $gpg -Arguments @('--batch', '--homedir', $tempHome, '--import', $KeyFile)
    } else {
        Write-Host '[CONFIGURE] Generating a dedicated sign-only sandbox GPG key'
        Invoke-Checked -Program $gpg -Arguments @(
            '--batch',
            '--homedir', $tempHome,
            '--pinentry-mode', 'loopback',
            '--passphrase', '',
            '--quick-generate-key', $Identity,
            'ed25519',
            'sign',
            '1y'
        )
        $fingerprint = Get-FirstFingerprint -GpgProgram $gpg -GpgHome $tempHome
        $secretKey = (& $gpg --batch --homedir $tempHome --armor --export-secret-keys $fingerprint) -join [Environment]::NewLine
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($secretKey)) {
            throw 'Unable to export the dedicated sandbox signing key.'
        }
        Set-TeckHostSecretFile -Path $KeyFile -Content ($secretKey + [Environment]::NewLine)
    }

    $fingerprint = Get-FirstFingerprint -GpgProgram $gpg -GpgHome $tempHome
    $publicKey = (& $gpg --batch --homedir $tempHome --armor --export $fingerprint) -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($publicKey)) {
        throw 'Unable to export the sandbox signing public key.'
    }
    [System.IO.File]::WriteAllText($publicKeyFile, $publicKey + [Environment]::NewLine, (New-Object System.Text.UTF8Encoding($false)))

    $sandboxPayload = Join-Path $tempHome 'signing-check.txt'
    $sandboxSignature = $sandboxPayload + '.sig'
    [System.IO.File]::WriteAllText($sandboxPayload, 'teck-sandbox-gpg-signing-check')
    Invoke-Checked -Program $gpg -Arguments @(
        '--batch',
        '--homedir', $tempHome,
        '--pinentry-mode', 'loopback',
        '--passphrase', '',
        '--yes',
        '--local-user', $fingerprint,
        '--output', $sandboxSignature,
        '--detach-sign', $sandboxPayload
    )

    Write-Host '[CONFIGURE] Importing the sandbox public key for local verification'
    Invoke-Checked -Program $gpg -Arguments @('--batch', '--import', $publicKeyFile)

    if (-not $SkipGitHubRegistration) {
        $gh = (Get-Command gh -ErrorAction Stop).Source
        $registeredJson = & $gh api user/gpg_keys 2>$null
        if ($LASTEXITCODE -ne 0) {
            throw 'GitHub CLI needs the admin:gpg_key scope. Run `gh auth refresh -h github.com -s admin:gpg_key`, then rerun this script.'
        }
        $registered = @($registeredJson | ConvertFrom-Json)
        $shortKeyId = $fingerprint.Substring($fingerprint.Length - 16)
        $registeredKey = $registered | Where-Object { $_.key_id -eq $shortKeyId } | Select-Object -First 1
        if (-not $registeredKey) {
            $payloadFile = Join-Path $tempHome 'github-gpg-key.json'
            [System.IO.File]::WriteAllText(
                $payloadFile,
                (@{ armored_public_key = $publicKey } | ConvertTo-Json),
                (New-Object System.Text.UTF8Encoding($false))
            )
            Invoke-Checked -Program $gh -Arguments @('api', 'user/gpg_keys', '-X', 'POST', '--input', $payloadFile, '--silent')
            $registeredJson = & $gh api user/gpg_keys 2>$null
            if ($LASTEXITCODE -ne 0) {
                throw 'Unable to verify the sandbox GPG key after GitHub registration.'
            }
            $registered = @($registeredJson | ConvertFrom-Json)
            $registeredKey = $registered | Where-Object { $_.key_id -eq $shortKeyId } | Select-Object -First 1
        }
        $verifiedEmails = @($registeredKey.emails | Where-Object { $_.verified -eq $true })
        if (-not $registeredKey -or $registeredKey.can_sign -ne $true -or $verifiedEmails.Count -eq 0) {
            throw "GitHub did not accept sandbox key $shortKeyId as a signing key with a verified email."
        }
    }

    Write-Host "[PASS] Windows commits use GPG key $windowsSigningKey" -ForegroundColor Green
    Write-Host "[PASS] Docker sandbox commits use dedicated GPG key $fingerprint" -ForegroundColor Green
    Write-Host 'Recreate existing Docker sandboxes so the lifecycle can install the signing key.'
} finally {
    Remove-Item -LiteralPath $tempHome -Recurse -Force -ErrorAction SilentlyContinue
}

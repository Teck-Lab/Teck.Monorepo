#requires -Version 5.1
<#
.SYNOPSIS
    Behavioral check for Set-OmniRouteCredentialFile (the write path shared
    by setup-host.ps1 and omniroute-credential.ps1).

.DESCRIPTION
    Exercises the exact credential write path without network, prompts, or
    the real key:
      1. A first write creates the file with the expected env line, UTF-8
         without BOM, and a locked ACL (inheritance removed; current user
         Modify; SYSTEM and Administrators FullControl).
      2. A rotation over the legacy read-only-ACL state (the ACL written by
         the pre-fix setup-host.ps1, which made re-runs fail with
         UnauthorizedAccessException) succeeds and replaces the content.
      3. A second rotation succeeds and the final content/permissions are
         correct, with no staging files left behind.

    Everything runs under $env:TEMP in a unique directory; the real
    %USERPROFILE%\.config\teck\omniroute.env is never touched.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\orca-sbx\omniroute-credential.test.ps1
#>
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('orca-sbx-credential-' + [guid]::NewGuid().ToString('N'))
$credentialFile = Join-Path $testRoot 'omniroute.env'
$failures = 0

function Assert-Check {
    param([bool] $Condition, [string] $Message)
    if ($Condition) {
        Write-Host "[PASS] $Message" -ForegroundColor Green
    } else {
        Write-Host "[FAIL] $Message" -ForegroundColor Red
        $script:failures++
    }
}

function ConvertTo-Sid {
    param($Identity)
    $account = $Identity -as [System.Security.Principal.NTAccount]
    if ($null -ne $account) {
        return $account.Translate([System.Security.Principal.SecurityIdentifier])
    }
    return $Identity -as [System.Security.Principal.SecurityIdentifier]
}

# Dot-source the function under test exactly as setup-host.ps1 does.
. (Join-Path $PSScriptRoot 'omniroute-credential.ps1')

$currentUserSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
$systemSid = [System.Security.Principal.SecurityIdentifier]::new('S-1-5-18')
$administratorsSid = [System.Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
$modifyRights = [System.Security.AccessControl.FileSystemRights]::Modify
$fullControlRights = [System.Security.AccessControl.FileSystemRights]::FullControl

function Assert-LockedAcl {
    param([string] $Path, [string] $Stage)
    $acl = Get-Acl -LiteralPath $Path
    Assert-Check $acl.AreAccessRulesProtected "$Stage ACL has inheritance removed"
    $expected = @(
        @{ Sid = $currentUserSid; Rights = $modifyRights },
        @{ Sid = $systemSid; Rights = $fullControlRights },
        @{ Sid = $administratorsSid; Rights = $fullControlRights }
    )
    $actual = @($acl.Access | ForEach-Object {
        @{ Sid = (ConvertTo-Sid $_.IdentityReference).Value; Rights = $_.FileSystemRights }
    })
    $granted = @($actual | Where-Object { $_.Rights -band [System.Security.AccessControl.FileSystemRights]::Read })
    Assert-Check ($actual.Count -eq 3) "$Stage ACL grants exactly 3 ACEs (got $($actual.Count))"
    foreach ($expectation in $expected) {
        $grant = $actual | Where-Object { $_.Sid -eq $expectation.Sid.Value }
        Assert-Check ($null -ne $grant) "$Stage ACL grants $($expectation.Sid.Value)"
        if ($null -ne $grant) {
            Assert-Check (($grant.Rights -band $expectation.Rights) -eq $expectation.Rights) "$Stage ACL grants $($expectation.Sid.Value) the required rights"
        }
    }
    $tooBroad = $granted | Where-Object {
        $_.Sid -ne $currentUserSid.Value -and $_.Sid -ne $systemSid.Value -and $_.Sid -ne $administratorsSid.Value
    }
    Assert-Check ($null -eq $tooBroad) "$Stage ACL has no broad or unexpected read grant"
}

try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null

    # 1. Fresh write.
    $firstKey = 'first-rotation-key-do-not-use'
    Set-OmniRouteCredentialFile -Path $credentialFile -ApiKey $firstKey
    Assert-Check (Test-Path -LiteralPath $credentialFile) 'first write creates the credential file'
    $expectedFirst = "OMNIROUTE_API_KEY=$firstKey" + [Environment]::NewLine
    Assert-Check ([System.IO.File]::ReadAllText($credentialFile) -eq $expectedFirst) 'first write stores the expected env line'
    $firstBytes = [System.IO.File]::ReadAllBytes($credentialFile)
    Assert-Check (-not ($firstBytes.Length -ge 3 -and $firstBytes[0] -eq 0xEF -and $firstBytes[1] -eq 0xBB -and $firstBytes[2] -eq 0xBF)) 'first write is UTF-8 without BOM'
    Assert-LockedAcl $credentialFile 'first write'

    # 2. Regression: rotation over the legacy pre-fix ACL (current user
    #    read-only) must succeed and restore the writable locked ACL.
    $legacy = New-Object System.Security.AccessControl.FileSecurity
    $legacy.SetAccessRuleProtection($true, $false)
    $legacy.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($currentUserSid, 'Read', 'Allow')))
    [System.IO.File]::SetAccessControl($credentialFile, $legacy)
    $rotatedKey = 'rotated-key-second-do-not-use'
    Set-OmniRouteCredentialFile -Path $credentialFile -ApiKey $rotatedKey
    $expectedRotated = "OMNIROUTE_API_KEY=$rotatedKey" + [Environment]::NewLine
    Assert-Check ([System.IO.File]::ReadAllText($credentialFile) -eq $expectedRotated) 'rotation over the legacy read-only ACL replaces the content'
    Assert-LockedAcl $credentialFile 'rotated'

    # 3. Ordinary rotation is idempotent and leaves no staging files.
    $finalKey = 'final-key-third-do-not-use'
    Set-OmniRouteCredentialFile -Path $credentialFile -ApiKey $finalKey
    $expectedFinal = "OMNIROUTE_API_KEY=$finalKey" + [Environment]::NewLine
    Assert-Check ([System.IO.File]::ReadAllText($credentialFile) -eq $expectedFinal) 'second rotation stores the new key'
    Assert-LockedAcl $credentialFile 'second rotation'
    $staging = @(Get-ChildItem -LiteralPath $testRoot -Filter 'omniroute.env.*.tmp' -File -ErrorAction SilentlyContinue)
    Assert-Check ($staging.Count -eq 0) 'no staging files remain after the rotations'
} finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures -gt 0) {
    Write-Host "$failures check(s) failed" -ForegroundColor Red
    exit 1
}
Write-Host 'All omniroute credential checks passed' -ForegroundColor Green
exit 0

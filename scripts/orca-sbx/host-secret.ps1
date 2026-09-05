#requires -Version 5.1
<#
.SYNOPSIS
    Defines Set-TeckHostSecretFile, the shared write path for host-only
    sandbox credentials.

.DESCRIPTION
    Dot-source this file to define the function. Host setup scripts and the
    behavioral check share this code path.

    The write is staged and atomic: content is written to a uniquely named
    sibling file whose ACL is locked before the first byte is written (no
    inherited ACEs; current user Modify; SYSTEM and Administrators
    FullControl), flushed to disk, then renamed over the destination on the
    same volume. An interrupted run therefore leaves either the previous
    secret or the new one - never a truncated or broadly readable secret.

    The destination ACL is reasserted before the rename so a host whose
    secret file was locked read-only by an older setup can still rotate it.

    Secret content is an in-process parameter only: it is never placed on a
    command line, written to the console, or logged by this function.
#>
function Set-TeckHostSecretFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Content
    )

    $ErrorActionPreference = 'Stop'

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $parent = Split-Path -Parent $Path
    if (-not $parent) {
        throw "Set-TeckHostSecretFile requires an absolute Path; got '$Path'."
    }

    # Well-known SIDs keep the ACL locale-independent (BUILTIN\Administrators
    # is localized, e.g. "Administratorer" on Danish Windows). The current
    # user keeps Modify so re-runs/rotation can replace the file; SYSTEM and
    # Administrators retain the same control they inherit on any profile
    # file; every other account is excluded because inheritance is removed.
    $currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
    $system = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-18')
    $administrators = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-544')

    $security = New-Object System.Security.AccessControl.FileSecurity
    $security.SetAccessRuleProtection($true, $false)
    $security.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($currentUser, 'Modify', 'Allow')))
    $security.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($system, 'FullControl', 'Allow')))
    $security.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($administrators, 'FullControl', 'Allow')))

    New-Item -ItemType Directory -Path $parent -Force | Out-Null

    # Remove staging files left behind by interrupted rotations.
    $fileName = [System.IO.Path]::GetFileName($Path)
    Get-ChildItem -LiteralPath $parent -Filter ($fileName + '.*.tmp') -File -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }

    # An existing destination may still carry the read-only grant written by
    # an older setup-host.ps1. The file owner can always rewrite its ACL, so
    # (re)assert the locked ACL first; Move-Item cannot replace a file the
    # current user cannot delete.
    if (Test-Path -LiteralPath $Path) {
        [System.IO.File]::SetAccessControl($Path, $security)
    }

    $staging = Join-Path $parent ($fileName + '.' + [guid]::NewGuid().ToString('N') + '.tmp')
    $stream = $null
    try {
        # Create the staging file with its locked ACL applied at creation so
        # the credential never exists under inherited, broadly readable
        # permissions - not even for an instant.
        $stream = New-Object System.IO.FileStream($staging,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None,
            4096,
            [System.IO.FileOptions]::None,
            $security)
        $bytes = $utf8NoBom.GetBytes($Content)
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)
        $stream.Dispose()
        $stream = $null

        # Same-volume rename: atomic. The destination keeps the staging
        # file's locked ACL, and an interrupted run can never leave a
        # half-written credential at the destination path.
        Move-Item -LiteralPath $staging -Destination $Path -Force
        $staging = $null
    } finally {
        if ($null -ne $stream) {
            $stream.Dispose()
        }
        if ($null -ne $staging -and (Test-Path -LiteralPath $staging)) {
            Remove-Item -LiteralPath $staging -Force -ErrorAction SilentlyContinue
        }
    }
}

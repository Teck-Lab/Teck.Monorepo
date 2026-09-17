[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $OmpArguments
)

$ErrorActionPreference = 'Stop'

# This bootstrap is placed once on the host at:
#   C:\Users\<you>\.paseo\omp-wrapper-bootstrap.ps1
# It is NOT repo-specific. It finds the repo-local wrapper at:
#   <cwd>\scripts\omp-wrapper.ps1
# and forwards all arguments to it.

$cwd = [IO.Path]::GetFullPath((Get-Location).Path)
$wrapper = Join-Path $cwd 'scripts\omp-wrapper.ps1'

if (-not (Test-Path -LiteralPath $wrapper -PathType Leaf)) {
    throw "Could not find repo-local OMP wrapper: $wrapper`n`nCopy the 'scripts' folder from the Paseo sandbox template into this repository and commit it."
}

& $wrapper @OmpArguments
exit $LASTEXITCODE

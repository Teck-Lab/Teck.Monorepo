#requires -Version 5
& "$PSScriptRoot/local-launch.ps1" -Lifecycle suspend
exit $LASTEXITCODE

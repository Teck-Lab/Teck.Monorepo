@echo off
setlocal
pushd "%SystemRoot%" >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0local-resume.ps1"
set "ORCA_EXIT=%ERRORLEVEL%"
popd
endlocal & exit /b %ORCA_EXIT%

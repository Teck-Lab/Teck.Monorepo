@echo off
setlocal
set WSLENV=ORCA_REPO_PATH/up
wsl.exe --exec bash -lc $ORCA_REPO_PATH/scripts/orca-vm/local-destroy.sh
set "ORCA_EXIT=%ERRORLEVEL%"
endlocal & exit /b %ORCA_EXIT%

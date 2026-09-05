@echo off
node "%~dp0lifecycle.mjs" destroy
exit /b %ERRORLEVEL%

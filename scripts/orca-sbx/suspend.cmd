@echo off
node "%~dp0lifecycle.mjs" suspend
exit /b %ERRORLEVEL%

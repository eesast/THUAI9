@echo off
setlocal
set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%\stop_thuai9_smoke.ps1" -Root "%ROOT%"
exit /b %ERRORLEVEL%

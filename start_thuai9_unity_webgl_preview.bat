@echo off
setlocal

set "ROOT=%~dp0"
set "WEB_PORT=18089"
set "WEBGL_ROOT=%ROOT%interface\Unity\Unity-WebGL"
set "ROOT_URL=http://127.0.0.1:%WEB_PORT%/"

echo [THUAI9] Starting Unity WebGL preview from interface\Unity\Unity-WebGL ...
echo [THUAI9] Preview URL: %ROOT_URL%

if not exist "%WEBGL_ROOT%\index.html" (
    echo [ERROR] WebGL root index not found: interface\Unity\Unity-WebGL\index.html
    exit /b 1
)

if not exist "%WEBGL_ROOT%\trial\index.html" (
    echo [ERROR] WebGL Trial entry not found: interface\Unity\Unity-WebGL\trial\index.html
    exit /b 1
)

if not exist "%WEBGL_ROOT%\live\index.html" (
    echo [ERROR] WebGL Live entry not found: interface\Unity\Unity-WebGL\live\index.html
    exit /b 1
)

if not exist "%WEBGL_ROOT%\playback\index.html" (
    echo [ERROR] WebGL Playback entry not found: interface\Unity\Unity-WebGL\playback\index.html
    exit /b 1
)

where python >nul 2>nul
if errorlevel 1 (
    where py >nul 2>nul
    if errorlevel 1 (
        echo [ERROR] python or py not found.
        exit /b 1
    )
    set "PYTHON_CMD=py -3"
) else (
    set "PYTHON_CMD=python"
)

call :StopWebPort

echo [THUAI9] Launching http.server from:
echo        %WEBGL_ROOT%
start "THUAI9 WebGL Preview" cmd /k "cd /d ""%WEBGL_ROOT%"" && %PYTHON_CMD% -m http.server %WEB_PORT% --bind 127.0.0.1"

echo [THUAI9] Waiting for WebGL preview server...
call :VerifyWebglRoot
if errorlevel 1 (
    echo [ERROR] WebGL preview failed validation.
    echo [HINT] Do not run python -m http.server from the repository root.
    exit /b 1
)

if not defined THUAI9_SKIP_BROWSER_LAUNCH (
    start "" "%ROOT_URL%"
) else (
    echo [THUAI9] THUAI9_SKIP_BROWSER_LAUNCH is set; not opening browser.
)

echo [THUAI9] WebGL preview is ready.
echo        %ROOT_URL%
echo        %ROOT_URL%trial/
echo        %ROOT_URL%live/
echo        %ROOT_URL%playback/

endlocal
exit /b 0

:StopWebPort
powershell -NoProfile -ExecutionPolicy Bypass -Command "$conns=Get-NetTCPConnection -LocalPort %WEB_PORT% -State Listen -ErrorAction SilentlyContinue; foreach($conn in $conns){ $owner=$conn.OwningProcess; if($owner -and $owner -ne $PID){ Stop-Process -Id $owner -Force -ErrorAction SilentlyContinue } }"
exit /b 0

:VerifyWebglRoot
powershell -NoProfile -ExecutionPolicy Bypass -Command "$deadline=(Get-Date).AddSeconds(30); while((Get-Date) -lt $deadline){ try { $root=Invoke-WebRequest -UseBasicParsing '%ROOT_URL%' -TimeoutSec 2; $trial=Invoke-WebRequest -UseBasicParsing '%ROOT_URL%trial/' -TimeoutSec 2; $live=Invoke-WebRequest -UseBasicParsing '%ROOT_URL%live/' -TimeoutSec 2; $playback=Invoke-WebRequest -UseBasicParsing '%ROOT_URL%playback/' -TimeoutSec 2; if($root.Content -match 'Directory listing for /' -or $root.Content -match '\.git|logic/|tasks/'){ exit 2 }; if($trial.StatusCode -eq 200 -and $live.StatusCode -eq 200 -and $playback.StatusCode -eq 200){ exit 0 } } catch { Start-Sleep -Milliseconds 500 } }; exit 1"
exit /b %ERRORLEVEL%

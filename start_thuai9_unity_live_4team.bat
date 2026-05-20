@echo off
setlocal

set "ROOT=%~dp0"
set "SERVER_PORT=8888"
set "SERVER_IP=127.0.0.1"
set "TEAM_COUNT=4"
set "CHARACTER_NUM=5"
set "GAME_SECONDS=600"
set "WEB_PORT=18089"
set "WS_PORT=18091"
set "WEBGL_ROOT=%ROOT%interface\Unity\Unity-WebGL"
set "BRIDGE_PROJECT=%WEBGL_ROOT%\tools\LiveWebSocketBridge\LiveWebSocketBridge.csproj"
set "BRIDGE_SOURCE=%WEBGL_ROOT%\tools\LiveWebSocketBridge\Program.cs"
set "BRIDGE_PROTO_ROOT=%ROOT%dependency\proto"
set "BRIDGE_BUILD_DIR=%TEMP%\THUAI9-LiveWebSocketBridge"
set "BRIDGE_BUILD_PROJECT=%BRIDGE_BUILD_DIR%\LiveWebSocketBridge.csproj"
set "BRIDGE_DLL=%BRIDGE_BUILD_DIR%\bin\Debug\net8.0\LiveWebSocketBridge.dll"
set "SERVER_LOG=%ROOT%logic\Server\logs\GameServer.log"
if not defined THUAI9_SPECTATOR_WAIT_SECONDS set "THUAI9_SPECTATOR_WAIT_SECONDS=180"

set "LIVE_URL=http://127.0.0.1:%WEB_PORT%/live/index.html?ws=ws://127.0.0.1:%WS_PORT%/live"

echo [THUAI9] Starting WebGL Live smoke: Server + WebSocket bridge + browser WebGL, then 4 ClientTest2 teams.
echo [THUAI9] Live URL: %LIVE_URL%

if not exist "%ROOT%logic\Server\Server.csproj" (
    echo [ERROR] Server project not found: logic\Server\Server.csproj
    exit /b 1
)

if not exist "%ROOT%logic\ClientTest2\ClientTest2.csproj" (
    echo [ERROR] ClientTest2 project not found: logic\ClientTest2\ClientTest2.csproj
    exit /b 1
)

if not exist "%WEBGL_ROOT%\live\index.html" (
    echo [ERROR] WebGL Live build not found: interface\Unity\Unity-WebGL\live\index.html
    echo [HINT] Export Unity-Live WebGL before running this smoke script.
    exit /b 1
)

if not exist "%BRIDGE_PROJECT%" (
    echo [ERROR] Live WebSocket bridge project not found: %BRIDGE_PROJECT%
    exit /b 1
)

if not exist "%BRIDGE_SOURCE%" (
    echo [ERROR] Live WebSocket bridge source not found: %BRIDGE_SOURCE%
    exit /b 1
)

if not exist "%BRIDGE_PROTO_ROOT%\Services.proto" (
    echo [ERROR] Proto files not found: %BRIDGE_PROTO_ROOT%
    exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] dotnet not found.
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

echo [THUAI9] Stopping previous smoke processes from this workspace...
if not defined THUAI9_SKIP_SMOKE_CLEANUP (
    call "%ROOT%stop_thuai9_smoke.bat"
    if errorlevel 1 (
        echo [ERROR] Failed to stop old smoke processes.
        exit /b 1
    )
) else (
    echo [THUAI9] THUAI9_SKIP_SMOKE_CLEANUP is set; keeping existing processes.
)
call :StopWebPorts

echo [THUAI9] Building Server, ClientTest2, and WebGL Live bridge before launch...
dotnet build "%ROOT%logic\Server\Server.csproj"
if errorlevel 1 (
    echo [ERROR] Server build failed.
    exit /b 1
)
dotnet build "%ROOT%logic\ClientTest2\ClientTest2.csproj"
if errorlevel 1 (
    echo [ERROR] ClientTest2 build failed.
    exit /b 1
)
call :PrepareBridgeBuildProject
if errorlevel 1 (
    echo [ERROR] Failed to prepare WebGL Live bridge build project.
    exit /b 1
)
dotnet build "%BRIDGE_BUILD_PROJECT%"
if errorlevel 1 (
    echo [ERROR] WebGL Live bridge build failed.
    exit /b 1
)
if not exist "%BRIDGE_DLL%" (
    echo [ERROR] WebGL Live bridge output not found: %BRIDGE_DLL%
    exit /b 1
)

if exist "%SERVER_LOG%" del /q "%SERVER_LOG%" >nul 2>nul

echo [THUAI9] Starting Server on %SERVER_IP%:%SERVER_PORT% ...
start "THUAI9 Server" cmd /k "cd /d ""%ROOT%logic\Server"" && dotnet run --no-build -- --port %SERVER_PORT% --teamCount %TEAM_COUNT% --CharacterNum %CHARACTER_NUM% --gameTimeInSecond %GAME_SECONDS% --fileName unity_webgl_live_smoke"

echo [THUAI9] Waiting for server to listen on %SERVER_IP%:%SERVER_PORT%...
powershell -NoProfile -Command "$deadline = (Get-Date).AddMinutes(2); while((Get-Date) -lt $deadline){ try { $c = [System.Net.Sockets.TcpClient]::new('%SERVER_IP%', %SERVER_PORT%); $c.Close(); exit 0 } catch { Start-Sleep -Seconds 1 } }; exit 1"
if errorlevel 1 (
    echo [ERROR] Server did not become ready on %SERVER_IP%:%SERVER_PORT%
    exit /b 1
)

echo [THUAI9] Starting WebGL static server on http://127.0.0.1:%WEB_PORT% from interface\Unity\Unity-WebGL ...
start "THUAI9 WebGL HTTP" cmd /k "cd /d ""%WEBGL_ROOT%"" && %PYTHON_CMD% -m http.server %WEB_PORT% --bind 127.0.0.1"

echo [THUAI9] Waiting for WebGL static server...
powershell -NoProfile -Command "$deadline = (Get-Date).AddSeconds(30); while((Get-Date) -lt $deadline){ try { $root = Invoke-WebRequest -UseBasicParsing 'http://127.0.0.1:%WEB_PORT%/' -TimeoutSec 2; $live = Invoke-WebRequest -UseBasicParsing 'http://127.0.0.1:%WEB_PORT%/live/index.html' -TimeoutSec 2; if($root.Content -match 'Directory listing for /' -or $root.Content -match '\.git|logic/|tasks/'){ exit 2 }; if($live.StatusCode -eq 200){ exit 0 } } catch { Start-Sleep -Milliseconds 500 } }; exit 1"
if errorlevel 2 (
    echo [ERROR] WebGL static server is serving the repository root, not interface\Unity\Unity-WebGL.
    echo [HINT] The script already stopped old listeners; check the "THUAI9 WebGL HTTP" window and port %WEB_PORT%.
    exit /b 1
)
if errorlevel 1 (
    echo [ERROR] WebGL static server did not become ready on 127.0.0.1:%WEB_PORT%
    exit /b 1
)

echo [THUAI9] Starting gRPC spectator to WebSocket bridge on ws://127.0.0.1:%WS_PORT%/live ...
start "THUAI9 WebGL Live Bridge" cmd /k "cd /d ""%ROOT%"" && dotnet ""%BRIDGE_DLL%"" --server %SERVER_IP%:%SERVER_PORT% --port %WS_PORT%"

echo [THUAI9] Opening browser WebGL Live page. The page auto-connects through ?ws=...
if not defined THUAI9_SKIP_BROWSER_LAUNCH (
    start "" "%LIVE_URL%"
) else (
    echo [THUAI9] THUAI9_SKIP_BROWSER_LAUNCH is set; not opening browser.
)

echo [THUAI9] Waiting for spectator registration before launching clients...
echo [THUAI9] Detection pattern: "A new spectator comes to watch this game" in logic\Server\logs\GameServer.log
powershell -NoProfile -ExecutionPolicy Bypass -Command "$log = '%SERVER_LOG%'; $pattern = 'A new spectator comes to watch this game'; $deadline = (Get-Date).AddSeconds([int]'%THUAI9_SPECTATOR_WAIT_SECONDS%'); while((Get-Date) -lt $deadline){ if(Test-Path -LiteralPath $log){ if(Select-String -LiteralPath $log -SimpleMatch $pattern -Quiet){ exit 0 } }; Start-Sleep -Seconds 1 }; exit 1"
if errorlevel 1 (
    echo [ERROR] No spectator was registered within %THUAI9_SPECTATOR_WAIT_SECONDS%s.
    echo [HINT] Check the "THUAI9 WebGL Live Bridge" window. Clients were NOT started.
    exit /b 1
)

echo [THUAI9] Spectator registered through WebGL Live bridge. Starting ClientTest2 teams...
for /L %%t in (1,1,%TEAM_COUNT%) do (
    start "THUAI9 ClientTest2 %%t" cmd /k "cd /d ""%ROOT%"" && dotnet run --no-build --project logic\ClientTest2\ClientTest2.csproj -- 0 %%t"
)

echo [THUAI9] WebGL Live smoke launch requested.
echo Keep Server, WebGL HTTP, WebGL Live Bridge, browser, and ClientTest2 windows open to inspect live rendering.

endlocal
exit /b 0

:StopWebPorts
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ports=@(%WEB_PORT%,%WS_PORT%); foreach($port in $ports){ $conns=Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue; foreach($conn in $conns){ $owner=$conn.OwningProcess; if($owner -and $owner -ne $PID){ Stop-Process -Id $owner -Force -ErrorAction SilentlyContinue } } }"
exit /b 0

:PrepareBridgeBuildProject
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $dir=$env:BRIDGE_BUILD_DIR; $project=$env:BRIDGE_BUILD_PROJECT; $proto=$env:BRIDGE_PROTO_ROOT; New-Item -ItemType Directory -Force -Path $dir | Out-Null; Copy-Item -LiteralPath $env:BRIDGE_SOURCE -Destination (Join-Path $dir 'Program.cs') -Force; Copy-Item -LiteralPath $env:BRIDGE_PROJECT -Destination $project -Force; $text = Get-Content -LiteralPath $project -Raw; $text = $text.Replace('..\..\..\..\dependency\proto\*.proto', ($proto + '\*.proto')).Replace('..\..\..\..\dependency\proto', $proto); Set-Content -LiteralPath $project -Value $text -Encoding utf8"
exit /b 0

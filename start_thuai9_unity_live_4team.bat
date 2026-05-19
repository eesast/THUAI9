@echo off
setlocal

set "ROOT=%~dp0"
set "SERVER_PORT=8888"
set "SERVER_IP=127.0.0.1"
set "TEAM_COUNT=4"
set "CHARACTER_NUM=5"
set "GAME_SECONDS=600"
set "UNITY_PROJECT=%ROOT%interface\Unity-Live"
set "UNITY_METHOD=SmokeTestLauncher.StartLiveSmoke"
set "SERVER_LOG=%ROOT%logic\Server\logs\GameServer.log"
if not defined THUAI9_SPECTATOR_WAIT_SECONDS set "THUAI9_SPECTATOR_WAIT_SECONDS=180"

echo [THUAI9] Starting Unity-Live smoke: Server first, wait for spectator, then launch 4 ClientTest2 teams.

if not exist "%ROOT%logic\Server\Server.csproj" (
    echo [ERROR] Server project not found: logic\Server\Server.csproj
    exit /b 1
)

if not exist "%ROOT%logic\ClientTest2\ClientTest2.csproj" (
    echo [ERROR] ClientTest2 project not found: logic\ClientTest2\ClientTest2.csproj
    exit /b 1
)

if not exist "%UNITY_PROJECT%\Assets\Scenes\Live.unity" (
    echo [ERROR] Unity-Live scene not found: interface\Unity-Live\Assets\Scenes\Live.unity
    exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] dotnet not found.
    exit /b 1
)

if not defined UNITY_EXE (
    call :FindUnity
)

if defined UNITY_EXE (
    if not exist "%UNITY_EXE%" (
        echo [WARN] UNITY_EXE does not exist: %UNITY_EXE%
        set "UNITY_EXE="
    )
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

echo [THUAI9] Building Server and ClientTest2 before launch...
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

if exist "%SERVER_LOG%" del /q "%SERVER_LOG%" >nul 2>nul

echo [THUAI9] Starting Server on %SERVER_IP%:%SERVER_PORT% ...
start "THUAI9 Server" cmd /k "cd /d ""%ROOT%logic\Server"" && dotnet run --no-build -- --port %SERVER_PORT% --teamCount %TEAM_COUNT% --CharacterNum %CHARACTER_NUM% --gameTimeInSecond %GAME_SECONDS% --fileName unity_live_smoke"

echo [THUAI9] Waiting for server to listen on %SERVER_IP%:%SERVER_PORT%...
powershell -NoProfile -Command "$deadline = (Get-Date).AddMinutes(2); while((Get-Date) -lt $deadline){ try { $c = [System.Net.Sockets.TcpClient]::new('%SERVER_IP%', %SERVER_PORT%); $c.Close(); exit 0 } catch { Start-Sleep -Seconds 1 } }; exit 1"
if errorlevel 1 (
    echo [ERROR] Server did not become ready on %SERVER_IP%:%SERVER_PORT%
    exit /b 1
)

if not defined THUAI9_SKIP_UNITY_LAUNCH (
    if defined UNITY_EXE (
        echo [THUAI9] Starting Unity-Live Play Mode. If it does not auto-connect, click "连接/等待" in Unity-Live.
        start "THUAI9 Unity-Live" "%UNITY_EXE%" -projectPath "%UNITY_PROJECT%" -executeMethod %UNITY_METHOD%
    ) else (
        echo [WARN] Unity Editor not found. Open interface\Unity-Live manually and click "连接/等待".
        echo [HINT] You can set UNITY_EXE, for example:
        echo        set "UNITY_EXE=D:\Program Files\2022.3.62f1c1\Editor\Unity.exe"
    )
) else (
    echo [THUAI9] THUAI9_SKIP_UNITY_LAUNCH is set; waiting for an already-open Unity-Live spectator.
)

echo [THUAI9] Waiting for spectator registration before launching clients...
echo [THUAI9] Detection pattern: "A new spectator comes to watch this game" in logic\Server\logs\GameServer.log
powershell -NoProfile -ExecutionPolicy Bypass -Command "$log = '%SERVER_LOG%'; $pattern = 'A new spectator comes to watch this game'; $deadline = (Get-Date).AddSeconds([int]'%THUAI9_SPECTATOR_WAIT_SECONDS%'); while((Get-Date) -lt $deadline){ if(Test-Path -LiteralPath $log){ if(Select-String -LiteralPath $log -SimpleMatch $pattern -Quiet){ exit 0 } }; Start-Sleep -Seconds 1 }; exit 1"
if errorlevel 1 (
    echo [ERROR] No spectator was registered within %THUAI9_SPECTATOR_WAIT_SECONDS%s.
    echo [HINT] Keep this window open, make sure Unity-Live is in Play Mode, then click "连接/等待" to 127.0.0.1:%SERVER_PORT%.
    echo [HINT] Clients were NOT started, so the game has not begun without a spectator.
    exit /b 1
)

echo [THUAI9] Spectator registered. Starting ClientTest2 teams...
for /L %%t in (1,1,%TEAM_COUNT%) do (
    start "THUAI9 ClientTest2 %%t" cmd /k "cd /d ""%ROOT%"" && dotnet run --no-build --project logic\ClientTest2\ClientTest2.csproj -- 0 %%t"
)

echo [THUAI9] Unity-Live smoke launch requested.
echo Keep the Server, Unity-Live, and ClientTest2 windows open to inspect live rendering.

endlocal
exit /b 0

:FindUnity
set "UNITY_VERSION=2022.3.62f1c1"
for %%U in (
    "D:\Program Files\%UNITY_VERSION%\Editor\Unity.exe"
    "D:\Program Files\Unity\Hub\Editor\%UNITY_VERSION%\Editor\Unity.exe"
    "D:\Program Files\Unity Hub\Editor\%UNITY_VERSION%\Editor\Unity.exe"
    "%ProgramFiles%\Unity\Hub\Editor\%UNITY_VERSION%\Editor\Unity.exe"
    "%ProgramFiles(x86)%\Unity\Hub\Editor\%UNITY_VERSION%\Editor\Unity.exe"
    "%LOCALAPPDATA%\Programs\Unity\Hub\Editor\%UNITY_VERSION%\Editor\Unity.exe"
) do (
    if exist "%%~fU" (
        set "UNITY_EXE=%%~fU"
        exit /b 0
    )
)

for /f "delims=" %%U in ('dir /b /s "D:\Program Files\*\Editor\Unity.exe" 2^>nul') do (
    set "UNITY_EXE=%%U"
    exit /b 0
)

for /f "delims=" %%U in ('dir /b /s "D:\Program Files\Unity\Hub\Editor\*\Editor\Unity.exe" 2^>nul') do (
    set "UNITY_EXE=%%U"
    exit /b 0
)

for /f "delims=" %%U in ('dir /b /s "D:\Program Files\Unity Hub\Editor\*\Editor\Unity.exe" 2^>nul') do (
    set "UNITY_EXE=%%U"
    exit /b 0
)

for /f "delims=" %%U in ('dir /b /s "%ProgramFiles%\Unity\Hub\Editor\*\Editor\Unity.exe" 2^>nul') do (
    set "UNITY_EXE=%%U"
    exit /b 0
)

for /f "delims=" %%U in ('dir /b /s "%ProgramFiles(x86)%\Unity\Hub\Editor\*\Editor\Unity.exe" 2^>nul') do (
    set "UNITY_EXE=%%U"
    exit /b 0
)

for /f "delims=" %%U in ('dir /b /s "%LOCALAPPDATA%\Programs\Unity\Hub\Editor\*\Editor\Unity.exe" 2^>nul') do (
    set "UNITY_EXE=%%U"
    exit /b 0
)

exit /b 0

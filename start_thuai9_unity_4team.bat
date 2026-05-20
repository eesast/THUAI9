@echo off
setlocal

set "ROOT=%~dp0"
set "SERVER_PORT=8888"
set "SERVER_IP=127.0.0.1"
set "UNITY_PROJECT=%ROOT%interface\Unity\Unity-Live"
set "UNITY_METHOD=SmokeTestLauncher.StartLiveSmoke"
if not defined UNITY_WARMUP_SECONDS set "UNITY_WARMUP_SECONDS=12"

echo [THUAI9] Launching Unity-Live smoke, then Server and 4 ClientTest2 teams...

if not exist "%ROOT%logic\Server\Server.csproj" (
    echo [ERROR] Server project not found: logic\Server\Server.csproj
    exit /b 1
)

if not exist "%ROOT%logic\ClientTest2\ClientTest2.csproj" (
    echo [ERROR] ClientTest2 project not found: logic\ClientTest2\ClientTest2.csproj
    exit /b 1
)

if not exist "%UNITY_PROJECT%\Assets\Scenes\Live.unity" (
    echo [ERROR] Unity Live scene not found: interface\Unity\Unity-Live\Assets\Scenes\Live.unity
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

if not defined UNITY_EXE (
    echo [ERROR] Unity Editor not found.
    if defined UNITY_HUB_EXE (
        echo [INFO] Unity Hub was found, but Unity Hub cannot run this smoke test by itself:
        echo        %UNITY_HUB_EXE%
    )
    echo [HINT] Set UNITY_EXE to your Unity.exe path, for example:
    echo        set "UNITY_EXE=D:\Program Files\2022.3.62f1c1\Editor\Unity.exe"
    echo        .\start_thuai9_unity_4team.bat
    exit /b 1
)

if not exist "%UNITY_EXE%" (
    echo [ERROR] UNITY_EXE does not exist: %UNITY_EXE%
    exit /b 1
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

echo [THUAI9] Starting Unity Editor Live Play Mode first...
echo [THUAI9] Unity will auto-connect and retry while waiting for the server.
start "THUAI9 Unity" "%UNITY_EXE%" -projectPath "%UNITY_PROJECT%" -executeMethod %UNITY_METHOD%

echo [THUAI9] Waiting %UNITY_WARMUP_SECONDS%s for Unity to load scene and enter Play Mode...
timeout /t %UNITY_WARMUP_SECONDS% /nobreak >nul

echo [THUAI9] Starting Server after Unity warmup...
start "THUAI9 Server" cmd /k "cd /d ""%ROOT%logic\Server"" && dotnet run --no-build -- --port %SERVER_PORT% --teamCount 4 --gameTimeInSecond 180 --fileName unity_smoke"
timeout /t 2 /nobreak >nul

echo [THUAI9] Waiting for server to listen on %SERVER_IP%:%SERVER_PORT%...
powershell -NoProfile -Command "$deadline = (Get-Date).AddMinutes(2); while((Get-Date) -lt $deadline){ try { $c = [System.Net.Sockets.TcpClient]::new('%SERVER_IP%', %SERVER_PORT%); $c.Close(); exit 0 } catch { Start-Sleep -Seconds 1 } }; exit 1"
if errorlevel 1 (
    echo [ERROR] Server did not become ready on %SERVER_IP%:%SERVER_PORT%
    exit /b 1
)

echo [THUAI9] Starting ClientTest2 teams...
for /L %%t in (1,1,4) do (
    start "THUAI9 ClientTest2 %%t" cmd /k "cd /d ""%ROOT%"" && dotnet run --no-build --project logic\ClientTest2\ClientTest2.csproj -- 0 %%t"
)

echo [THUAI9] Unity smoke launch requested.
echo Keep the Server, ClientTest2, and Unity windows open to inspect logs.

endlocal
exit /b 0

:FindUnity
set "UNITY_VERSION=2022.3.62f1c1"
if exist "D:\Program Files\Unity Hub\Unity Hub.exe" (
    set "UNITY_HUB_EXE=D:\Program Files\Unity Hub\Unity Hub.exe"
)
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

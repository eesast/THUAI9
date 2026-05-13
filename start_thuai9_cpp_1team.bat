@echo off
setlocal

set "ROOT=%~dp0"
set "SERVER_PORT=8888"
set "CAPI_EXE="

echo [THUAI9] Launching UI, server, and debug CAPI clients...

if not exist "%ROOT%interface\AvaloniaUI\THUAI9_Avalonia.csproj" (
    echo [ERROR] UI project not found: interface\AvaloniaUI\THUAI9_Avalonia.csproj
    exit /b 1
)

if not exist "%ROOT%logic\Server\Server.csproj" (
    echo [ERROR] Server project not found: logic\Server\Server.csproj
    exit /b 1
)

if exist "%ROOT%CAPI\cpp\x64\Debug\API.exe" (
    set "CAPI_EXE=%ROOT%CAPI\cpp\x64\Debug\API.exe"
) else if exist "%ROOT%CAPI\cpp\x64\Release\API.exe" (
    set "CAPI_EXE=%ROOT%CAPI\cpp\x64\Release\API.exe"
) else (
    echo [ERROR] CAPI executable not found.
    echo [ERROR] Build API.sln in Visual Studio 2022 first.
    exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] dotnet not found.
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

echo [THUAI9] Building UI and Server before launch...
dotnet build "%ROOT%interface\AvaloniaUI\THUAI9_Avalonia.csproj"
if errorlevel 1 (
    echo [ERROR] UI build failed.
    exit /b 1
)
dotnet build "%ROOT%logic\Server\Server.csproj"
if errorlevel 1 (
    echo [ERROR] Server build failed.
    exit /b 1
)

start "THUAI9 UI" cmd /k "cd /d ""%ROOT%interface\AvaloniaUI"" && dotnet run --no-build"
timeout /t 2 /nobreak >nul

start "THUAI9 Server" cmd /k "cd /d ""%ROOT%logic\Server"" && dotnet run --no-build -- --port %SERVER_PORT% --teamCount 2"
timeout /t 2 /nobreak >nul

echo [THUAI9] Waiting for server to listen on 127.0.0.1:%SERVER_PORT%...
powershell -NoProfile -Command "$deadline = (Get-Date).AddMinutes(2); while((Get-Date) -lt $deadline){ try { $c = [System.Net.Sockets.TcpClient]::new('127.0.0.1', %SERVER_PORT%); $c.Close(); exit 0 } catch { Start-Sleep -Seconds 1 } }; exit 1"
if errorlevel 1 (
    echo [ERROR] Server did not become ready on 127.0.0.1:%SERVER_PORT%
    exit /b 1
)

echo [THUAI9] Starting home clients (pid=0) for both teams...
start "THUAI9 CAPI 1-0" cmd /k ""%CAPI_EXE%" -t 1 -p 0 -I 127.0.0.1 -P %SERVER_PORT% -d -o"
start "THUAI9 CAPI 2-0" cmd /k ""%CAPI_EXE%" -t 2 -p 0 -I 127.0.0.1 -P %SERVER_PORT% -d -o"

echo [THUAI9] All home clients launched.
echo Character CAPI processes will start automatically when characters are created.

endlocal

@echo off
setlocal

set "ROOT=%~dp0"
set "SERVER_PORT=8888"
set "SERVER_IP=127.0.0.1"
if not defined GAME_TIME_SECONDS set "GAME_TIME_SECONDS=180"
if not defined REPLAY_FILE_NAME set "REPLAY_FILE_NAME=avalonia_4team"

echo [THUAI9] Launching Avalonia UI, Server, and 4 ClientTest2 teams...

if not exist "%ROOT%interface\AvaloniaUI\THUAI9_Avalonia.csproj" (
    echo [ERROR] Avalonia project not found: interface\AvaloniaUI\THUAI9_Avalonia.csproj
    exit /b 1
)

if not exist "%ROOT%logic\Server\Server.csproj" (
    echo [ERROR] Server project not found: logic\Server\Server.csproj
    exit /b 1
)

if not exist "%ROOT%logic\ClientTest2\ClientTest2.csproj" (
    echo [ERROR] ClientTest2 project not found: logic\ClientTest2\ClientTest2.csproj
    exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] dotnet not found.
    exit /b 1
)

echo [THUAI9] Starting Avalonia UI first; it will auto-connect and retry.
start "THUAI9 Avalonia" cmd /k "cd /d ""%ROOT%"" && dotnet run --project interface\AvaloniaUI\THUAI9_Avalonia.csproj"
timeout /t 2 /nobreak >nul

echo [THUAI9] Starting Server on %SERVER_IP%:%SERVER_PORT%...
start "THUAI9 Server" cmd /k "cd /d ""%ROOT%logic\Server"" && dotnet run -- --port %SERVER_PORT% --teamCount 4 --gameTimeInSecond %GAME_TIME_SECONDS% --fileName %REPLAY_FILE_NAME%"
timeout /t 2 /nobreak >nul

echo [THUAI9] Waiting for server to listen on %SERVER_IP%:%SERVER_PORT%...
powershell -NoProfile -Command "$deadline = (Get-Date).AddMinutes(2); while((Get-Date) -lt $deadline){ try { $c = [System.Net.Sockets.TcpClient]::new('%SERVER_IP%', %SERVER_PORT%); $c.Close(); exit 0 } catch { Start-Sleep -Seconds 1 } }; exit 1"
if errorlevel 1 (
    echo [ERROR] Server did not become ready on %SERVER_IP%:%SERVER_PORT%
    exit /b 1
)

echo [THUAI9] Starting ClientTest2 teams...
for /L %%t in (1,1,4) do (
    start "THUAI9 ClientTest2 %%t" cmd /k "cd /d ""%ROOT%"" && dotnet run --project logic\ClientTest2\ClientTest2.csproj -- 0 %%t"
)

echo [THUAI9] Avalonia 4-team smoke launch requested.
echo Keep the Avalonia, Server, and ClientTest2 windows open to inspect logs.

endlocal

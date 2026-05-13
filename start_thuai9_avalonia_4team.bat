@echo off
setlocal

set "ROOT=%~dp0"
set "SERVER_PORT=8888"
set "SERVER_IP=127.0.0.1"
if not defined GAME_TIME_SECONDS set "GAME_TIME_SECONDS=180"
if not defined REPLAY_FILE_NAME set "REPLAY_FILE_NAME=avalonia_4team"
if not defined AVALONIA_WARMUP_SECONDS set "AVALONIA_WARMUP_SECONDS=10"
if not defined AVALONIA_READY_TIMEOUT_SECONDS set "AVALONIA_READY_TIMEOUT_SECONDS=45"
set "AVALONIA_READY_FILE=%TEMP%\thuai9_avalonia_ready_%RANDOM%%RANDOM%.txt"
set "AVALONIA_EXE=%ROOT%interface\AvaloniaUI\bin\Debug\net8.0\THUAI9_Avalonia.exe"

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

echo [THUAI9] Building Avalonia UI before launch so startup does not miss the match opening...
dotnet build "%ROOT%interface\AvaloniaUI\THUAI9_Avalonia.csproj"
if errorlevel 1 (
    echo [ERROR] Avalonia build failed.
    exit /b 1
)

if exist "%AVALONIA_READY_FILE%" del /q "%AVALONIA_READY_FILE%" >nul 2>nul

echo [THUAI9] Starting Avalonia UI first; it will auto-connect and retry.
if exist "%AVALONIA_EXE%" (
    start "THUAI9 Avalonia" cmd /k "cd /d ""%ROOT%"" && set ""THUAI9_AVALONIA_READY_FILE=%AVALONIA_READY_FILE%"" && ""%AVALONIA_EXE%"""
) else (
    start "THUAI9 Avalonia" cmd /k "cd /d ""%ROOT%"" && set ""THUAI9_AVALONIA_READY_FILE=%AVALONIA_READY_FILE%"" && dotnet run --no-build --project interface\AvaloniaUI\THUAI9_Avalonia.csproj"
)
echo [THUAI9] Waiting %AVALONIA_WARMUP_SECONDS%s for Avalonia to enter its auto-connect loop...
timeout /t %AVALONIA_WARMUP_SECONDS% /nobreak >nul

echo [THUAI9] Starting Server on %SERVER_IP%:%SERVER_PORT%...
start "THUAI9 Server" cmd /k "cd /d ""%ROOT%logic\Server"" && dotnet run -- --port %SERVER_PORT% --teamCount 4 --gameTimeInSecond %GAME_TIME_SECONDS% --fileName %REPLAY_FILE_NAME%"
timeout /t 2 /nobreak >nul

echo [THUAI9] Waiting for server to listen on %SERVER_IP%:%SERVER_PORT%...
powershell -NoProfile -Command "$deadline = (Get-Date).AddMinutes(2); while((Get-Date) -lt $deadline){ try { $c = [System.Net.Sockets.TcpClient]::new('%SERVER_IP%', %SERVER_PORT%); $c.Close(); exit 0 } catch { Start-Sleep -Seconds 1 } }; exit 1"
if errorlevel 1 (
    echo [ERROR] Server did not become ready on %SERVER_IP%:%SERVER_PORT%
    exit /b 1
)

echo [THUAI9] Waiting for Avalonia spectator registration before clients start...
powershell -NoProfile -Command "$deadline = (Get-Date).AddSeconds(%AVALONIA_READY_TIMEOUT_SECONDS%); while((Get-Date) -lt $deadline){ if(Test-Path -LiteralPath '%AVALONIA_READY_FILE%'){ exit 0 }; Start-Sleep -Milliseconds 250 }; exit 1"
if errorlevel 1 (
    echo [ERROR] Avalonia did not report spectator readiness within %AVALONIA_READY_TIMEOUT_SECONDS%s.
    echo [ERROR] Not starting clients, otherwise the spectator may miss the opening seconds.
    exit /b 1
)

echo [THUAI9] Starting ClientTest2 teams...
for /L %%t in (1,1,4) do (
    start "THUAI9 ClientTest2 %%t" cmd /k "cd /d ""%ROOT%"" && dotnet run --project logic\ClientTest2\ClientTest2.csproj -- 0 %%t"
)

echo [THUAI9] Avalonia 4-team smoke launch requested.
echo Keep the Avalonia, Server, and ClientTest2 windows open to inspect logs.

endlocal

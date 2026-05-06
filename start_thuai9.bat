@echo off
setlocal

set "ROOT=%~dp0"

echo [THUAI9] Launching UI, server, and 4 clients...

if not exist "%ROOT%interface\AvaloniaUI\THUAI9_Avalonia.csproj" (
    echo [ERROR] UI project not found: interface\AvaloniaUI\THUAI9_Avalonia.csproj
    exit /b 1
)

if not exist "%ROOT%logic\Server\Server.csproj" (
    echo [ERROR] Server project not found: logic\Server\Server.csproj
    exit /b 1
)

if not exist "%ROOT%logic\ClientTest2\ClientTest2.csproj" (
    echo [ERROR] Client project not found: logic\ClientTest2\ClientTest2.csproj
    exit /b 1
)

start "THUAI9 UI" cmd /k "cd /d ""%ROOT%interface\AvaloniaUI"" && dotnet run"
timeout /t 2 /nobreak >nul

start "THUAI9 Server" cmd /k "cd /d ""%ROOT%logic\Server"" && dotnet run"
timeout /t 2 /nobreak >nul

for /L %%i in (1,1,4) do (
    start "THUAI9 Client %%i" cmd /k "cd /d ""%ROOT%logic\ClientTest2"" && dotnet run -- 0 %%i"
)

echo [THUAI9] All processes launched.
echo Keep each terminal open to inspect logs.

endlocal

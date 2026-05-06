@echo off
setlocal

set "ROOT=%~dp0"
set "CAPI_BUILD_DIR=%ROOT%CAPI\cpp\out\build\x64-Debug"
set "CMAKE_EXE=cmake"

echo [THUAI9] Launching UI, server, and 4 CAPI clients...

if not exist "%ROOT%interface\AvaloniaUI\THUAI9_Avalonia.csproj" (
    echo [ERROR] UI project not found: interface\AvaloniaUI\THUAI9_Avalonia.csproj
    exit /b 1
)

if not exist "%ROOT%logic\Server\Server.csproj" (
    echo [ERROR] Server project not found: logic\Server\Server.csproj
    exit /b 1
)

where cmake >nul 2>nul
if errorlevel 1 (
    if exist "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe" (
        set "CMAKE_EXE=C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
    ) else (
        echo [ERROR] CMake not found.
        exit /b 1
    )
)

echo [THUAI9] Building CAPI...
if "%CMAKE_EXE%"=="cmake" (
    cmake --build "%CAPI_BUILD_DIR%" --target capi
) else (
    "%CMAKE_EXE%" --build "%CAPI_BUILD_DIR%" --target capi
)
if errorlevel 1 (
    echo [ERROR] CAPI build failed.
    exit /b 1
)

start "THUAI9 UI" cmd /k "cd /d ""%ROOT%interface\AvaloniaUI"" && dotnet run"
timeout /t 2 /nobreak >nul

start "THUAI9 Server" cmd /k "cd /d ""%ROOT%logic\Server"" && dotnet run"
timeout /t 2 /nobreak >nul

echo [THUAI9] Waiting for server to listen on 127.0.0.1:8888...
powershell -NoProfile -Command "$deadline = (Get-Date).AddMinutes(2); while((Get-Date) -lt $deadline){ try { $c = [System.Net.Sockets.TcpClient]::new('127.0.0.1', 8888); $c.Close(); exit 0 } catch { Start-Sleep -Seconds 1 } }; exit 1"
if errorlevel 1 (
    echo [ERROR] Server did not become ready on 127.0.0.1:8888
    exit /b 1
)

for /L %%i in (1,1,4) do (
    start "THUAI9 CAPI %%i" cmd /k "cd /d ""%CAPI_BUILD_DIR%"" && capi.exe -t %%i -p 0 -I 127.0.0.1 -P 8888"
)

echo [THUAI9] All processes launched.
echo Keep each terminal open to inspect logs.

endlocal

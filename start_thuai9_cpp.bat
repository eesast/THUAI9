@echo off
setlocal enabledelayedexpansion

set "ROOT=%~dp0"
set "CAPI_EXE=%ROOT%CAPI\cpp\x64\Debug\API.exe"
set "SERVER_DIR=%ROOT%logic\Server"
set "UI_DIR=%ROOT%interface\AvaloniaUI"

if not exist "%CAPI_EXE%" (
    echo [THUAI9] CAPI exe not found, building...
    for /f "delims=" %%i in ('powershell -NoProfile -Command "& 'D:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe' -latest -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1"') do set "MSBUILD=%%i"
    if not defined MSBUILD (
        echo [ERROR] MSBuild not found. Please build CAPI manually in Visual Studio first.
        pause
        exit /b 1
    )
    "!MSBUILD!" "%ROOT%CAPI\cpp\API.sln" -p:Configuration=Debug -p:Platform=x64 -t:Build -m -verbosity:minimal
    if errorlevel 1 (
        echo [ERROR] CAPI build failed.
        pause
        exit /b 1
    )
    echo [THUAI9] Build done.
)

echo [THUAI9] Starting UI...
start "THUAI9 UI" cmd /k "cd /d ""%UI_DIR%"" && dotnet run"

echo [THUAI9] Starting Server...
start "THUAI9 Server" cmd /k "cd /d ""%SERVER_DIR%"" && dotnet run -- --port 8888 --teamCount 4"

echo [THUAI9] Waiting for server on 127.0.0.1:8888...
powershell -NoProfile -Command "$deadline = (Get-Date).AddMinutes(2); while((Get-Date) -lt $deadline){ try { $c = [System.Net.Sockets.TcpClient]::new('127.0.0.1', 8888); $c.Close(); exit 0 } catch { Start-Sleep -Seconds 1 } }; exit 1"
if errorlevel 1 (
    echo [ERROR] Server did not become ready on 127.0.0.1:8888
    pause
    exit /b 1
)

echo [THUAI9] Starting 16 CAPI clients (4 teams x 4 players)...
for /L %%i in (1,1,4) do (
    start "CAPI Team%%i-Player0" cmd /k "cd /d ""%ROOT%CAPI\cpp\x64\Debug"" && API.exe -t %%i -p 0 -I 127.0.0.1 -P 8888 -d"
    for /L %%j in (1,1,3) do (
        start "CAPI Team%%i-Player%%j" cmd /k "cd /d ""%ROOT%CAPI\cpp\x64\Debug"" && API.exe -t %%i -p %%j -I 127.0.0.1 -P 8888 -d"
    )
)

echo [THUAI9] All processes launched.
echo.
echo ============================================
echo   Close any terminal to stop that client.
echo   Close the Server terminal to end game.
echo   Edit AI logic in: CAPI\cpp\API\src\AI.cpp
echo ============================================
echo.
pause
endlocal

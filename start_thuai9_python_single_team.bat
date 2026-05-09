@echo off
setlocal

set "ROOT=%~dp0"
set "PY_ROOT=%ROOT%CAPI\python"
set "PY_MAIN=%PY_ROOT%\PyAPI\main.py"
set "PY_PROTO=%PY_ROOT%\proto\Services_pb2_grpc.py"
set "SERVER_PROJ=%ROOT%logic\Server\Server.csproj"
set "UI_PROJ=%ROOT%interface\AvaloniaUI\THUAI9_Avalonia.csproj"
set "UI_WORKDIR=%ROOT%interface\AvaloniaUI"
set "UI_EXE=%UI_WORKDIR%\bin\Debug\net8.0\THUAI9_Avalonia.exe"

if not defined PYTHON_EXE set "PYTHON_EXE=python"
if not defined SERVER_IP set "SERVER_IP=127.0.0.1"
if not defined SERVER_PORT set "SERVER_PORT=8888"
if not defined ACTIVE_TEAM set "ACTIVE_TEAM=1"
if not defined DUMMY_TEAM set "DUMMY_TEAM=2"
if not defined GAME_TIME set "GAME_TIME=120"
if not defined ENABLE_UI set "ENABLE_UI=1"
if not defined PY_FLAGS set "PY_FLAGS=-o -d"

echo [THUAI9] Launching single active team Python test...

if not exist "%PY_MAIN%" (
    echo [ERROR] Python client entry not found: %PY_MAIN%
    exit /b 1
)

if not exist "%SERVER_PROJ%" (
    echo [ERROR] Server project not found: %SERVER_PROJ%
    exit /b 1
)

where "%PYTHON_EXE%" >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Python not found: %PYTHON_EXE%
    exit /b 1
)

if not exist "%PY_PROTO%" (
    echo [THUAI9] Python proto files not found, generating...
    pushd "%PY_ROOT%"
    call generate_proto.cmd
    if errorlevel 1 (
        popd
        echo [ERROR] Failed to generate Python proto files.
        exit /b 1
    )
    popd
)

if "%ENABLE_UI%"=="1" (
    if exist "%UI_PROJ%" (
        echo [THUAI9] Building Avalonia UI...
        pushd "%UI_WORKDIR%"
        dotnet build
        if errorlevel 1 (
            popd
            echo [ERROR] Avalonia UI build failed.
            exit /b 1
        )
        popd

        if not exist "%UI_EXE%" (
            echo [ERROR] Avalonia UI executable not found: %UI_EXE%
            exit /b 1
        )

        start "THUAI9 UI" /D "%UI_WORKDIR%\bin\Debug\net8.0" "THUAI9_Avalonia.exe"
        timeout /t 2 /nobreak >nul
    ) else (
        echo [WARN] UI project not found, skip UI.
    )
)

start "THUAI9 Server" /D "%ROOT%logic\Server" cmd /k "dotnet run -- --port %SERVER_PORT% --teamCount 2 --gameTimeInSecond %GAME_TIME%"
timeout /t 2 /nobreak >nul

echo [THUAI9] Waiting for server to listen on %SERVER_IP%:%SERVER_PORT%...
powershell -NoProfile -Command "$deadline = (Get-Date).AddMinutes(2); while((Get-Date) -lt $deadline){ try { $c = [System.Net.Sockets.TcpClient]::new('%SERVER_IP%', %SERVER_PORT%); $c.Close(); exit 0 } catch { Start-Sleep -Seconds 1 } }; exit 1"
if errorlevel 1 (
    echo [ERROR] Server did not become ready on %SERVER_IP%:%SERVER_PORT%.
    exit /b 1
)

echo [THUAI9] Launch active team controller: team %ACTIVE_TEAM%, player 0
start "THUAI9 Team %ACTIVE_TEAM% P0" /D "%PY_ROOT%" cmd /k ""%PYTHON_EXE%" -m PyAPI.main -I %SERVER_IP% -P %SERVER_PORT% -t %ACTIVE_TEAM% -p 0 %PY_FLAGS%"
timeout /t 1 /nobreak >nul

echo [THUAI9] Launch active team worker: team %ACTIVE_TEAM%, player 1
start "THUAI9 Team %ACTIVE_TEAM% P1" /D "%PY_ROOT%" cmd /k ""%PYTHON_EXE%" -m PyAPI.main -I %SERVER_IP% -P %SERVER_PORT% -t %ACTIVE_TEAM% -p 1 %PY_FLAGS%"
timeout /t 2 /nobreak >nul

echo [THUAI9] Launch dummy team placeholder: team %DUMMY_TEAM%, player 0, IdleAI
start "THUAI9 Team %DUMMY_TEAM% Idle" /D "%PY_ROOT%" cmd /k ""%PYTHON_EXE%" -m PyAPI.main -I %SERVER_IP% -P %SERVER_PORT% -t %DUMMY_TEAM% -p 0 --aiModule PyAPI.IdleAI %PY_FLAGS%"

echo [THUAI9] All processes launched.
echo [THUAI9] Active team: %ACTIVE_TEAM% (player 0 + player 1)
echo [THUAI9] Dummy team : %DUMMY_TEAM% (player 0 only, no actions)

endlocal

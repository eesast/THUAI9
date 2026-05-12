@echo off
setlocal

set "ROOT=%~dp0"
set "SERVER_PORT=8888"
set "SERVER_IP=127.0.0.1"
set "PY_ROOT=%ROOT%CAPI\python"
set "PY_MAIN=%PY_ROOT%\PyAPI\main.py"
set "SERVER_PROJ=%ROOT%logic\Server\Server.csproj"
set "UI_PROJ=%ROOT%interface\AvaloniaUI\THUAI9_Avalonia.csproj"

if not defined PYTHON_EXE set "PYTHON_EXE=python"
if not defined PY_AI_MODULE set "PY_AI_MODULE=PyAPI.AI"
if not defined TEAM1_AI_MODULE set "TEAM1_AI_MODULE=%PY_AI_MODULE%"
if not defined TEAM2_AI_MODULE set "TEAM2_AI_MODULE=%PY_AI_MODULE%"
if not defined TEAM3_AI_MODULE set "TEAM3_AI_MODULE=%PY_AI_MODULE%"
if not defined TEAM4_AI_MODULE set "TEAM4_AI_MODULE=%PY_AI_MODULE%"
if not defined PY_FLAGS set "PY_FLAGS=-d -o"

echo [THUAI9] Launching UI, server, and 4-team Python clients...

if not exist "%PY_MAIN%" (
    echo [ERROR] Python client entry not found: %PY_MAIN%
    exit /b 1
)

if not exist "%UI_PROJ%" (
    echo [ERROR] UI project not found: interface\AvaloniaUI\THUAI9_Avalonia.csproj
    exit /b 1
)

if not exist "%SERVER_PROJ%" (
    echo [ERROR] Server project not found: logic\Server\Server.csproj
    exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] dotnet not found.
    exit /b 1
)

where "%PYTHON_EXE%" >nul 2>nul
if errorlevel 1 (
    if not exist "%PYTHON_EXE%" (
        echo [ERROR] Python not found: %PYTHON_EXE%
        exit /b 1
    )
)

echo [THUAI9] Generating Python proto files...
pushd "%PY_ROOT%"
call generate_proto.cmd
if errorlevel 1 (
    popd
    echo [ERROR] Failed to generate Python proto files.
    exit /b 1
)
popd

start "THUAI9 UI" cmd /k "cd /d ""%ROOT%interface\AvaloniaUI"" && dotnet run"
timeout /t 2 /nobreak >nul

start "THUAI9 Server" cmd /k "cd /d ""%ROOT%logic\Server"" && dotnet run -- --port %SERVER_PORT% --teamCount 4"
timeout /t 2 /nobreak >nul

echo [THUAI9] Waiting for server to listen on %SERVER_IP%:%SERVER_PORT%...
powershell -NoProfile -Command "$deadline = (Get-Date).AddMinutes(2); while((Get-Date) -lt $deadline){ try { $c = [System.Net.Sockets.TcpClient]::new('%SERVER_IP%', %SERVER_PORT%); $c.Close(); exit 0 } catch { Start-Sleep -Seconds 1 } }; exit 1"
if errorlevel 1 (
    echo [ERROR] Server did not become ready on %SERVER_IP%:%SERVER_PORT%
    exit /b 1
)

echo [THUAI9] Starting team home clients (pid=0)...
start "THUAI9 Python 1-0" /D "%PY_ROOT%" cmd /k ""%PYTHON_EXE%" -m PyAPI.main -t 1 -p 0 -I %SERVER_IP% -P %SERVER_PORT% --aiModule %TEAM1_AI_MODULE% %PY_FLAGS%"
start "THUAI9 Python 2-0" /D "%PY_ROOT%" cmd /k ""%PYTHON_EXE%" -m PyAPI.main -t 2 -p 0 -I %SERVER_IP% -P %SERVER_PORT% --aiModule %TEAM2_AI_MODULE% %PY_FLAGS%"
start "THUAI9 Python 3-0" /D "%PY_ROOT%" cmd /k ""%PYTHON_EXE%" -m PyAPI.main -t 3 -p 0 -I %SERVER_IP% -P %SERVER_PORT% --aiModule %TEAM3_AI_MODULE% %PY_FLAGS%"
start "THUAI9 Python 4-0" /D "%PY_ROOT%" cmd /k ""%PYTHON_EXE%" -m PyAPI.main -t 4 -p 0 -I %SERVER_IP% -P %SERVER_PORT% --aiModule %TEAM4_AI_MODULE% %PY_FLAGS%"

echo [THUAI9] All home clients launched.
echo Character Python processes will start automatically when BuildCharacter succeeds.
echo Keep each terminal open to inspect logs.

endlocal

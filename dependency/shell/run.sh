#!/usr/bin/env bash
# =============================================================================
# File         : dependency/shell/run.sh
# Description  : THUAI9 container entrypoint — starts server or client
#
# Environment Variables:
#   TERMINAL         SERVER | CLIENT
#   SERVER_PORT      Server listen port (default: 8888)
#   TEAM_COUNT       Number of teams (default: 4)
#   GAME_TIME_SEC    Game duration in seconds (default: 600)
#   CHARACTER_NUM    Max characters per team (default: 6)
#   TEAM_ID          Current team id (1-based, CLIENT only)
#   SERVER_IP        Server address (CLIENT only, default: 127.0.0.1)
#   PLAYER_LANG      cpp | python (CLIENT only, default: python)
#   AI_MODULE        Python AI module path (default: PyAPI.AI)
#   LOG_LEVEL        Server log level 1-5 (default: 5)
# =============================================================================
set -euo pipefail

TERMINAL="${TERMINAL:-CLIENT}"
SERVER_PORT="${SERVER_PORT:-8888}"
TEAM_COUNT="${TEAM_COUNT:-4}"
GAME_TIME_SEC="${GAME_TIME_SEC:-600}"
CHARACTER_NUM="${CHARACTER_NUM:-6}"
TEAM_ID="${TEAM_ID:-1}"
SERVER_IP="${SERVER_IP:-127.0.0.1}"
PLAYER_LANG="${PLAYER_LANG:-python}"
AI_MODULE="${AI_MODULE:-PyAPI.AI}"
LOG_LEVEL="${LOG_LEVEL:-5}"
PLAYBACK_FILE="${PLAYBACK_FILE:-/usr/local/playback/mygame}"

RUN_DIR="/usr/local"
PLAYER_DIR="$RUN_DIR/PlayerCode"

# ──────────────────────────────────────────────
# SERVER
# ──────────────────────────────────────────────
if [ "$TERMINAL" = "SERVER" ]; then
    echo "========================================="
    echo "  THUAI9 Server"
    echo "  Port:     $SERVER_PORT"
    echo "  Teams:    $TEAM_COUNT"
    echo "  Duration: ${GAME_TIME_SEC}s"
    echo "========================================="

    SERVER_DLL="$RUN_DIR/Server/Server.dll"
    if [ ! -f "$SERVER_DLL" ]; then
        echo "ERROR: Server.dll not found at $SERVER_DLL" >&2
        exit 1
    fi

    mkdir -p /usr/local/team1 /usr/local/team2 /usr/local/team3 /usr/local/team4
    mkdir -p /usr/local/playback

    exec dotnet "$SERVER_DLL" \
        --port "$SERVER_PORT" \
        --teamCount "$TEAM_COUNT" \
        --gameTimeInSecond "$GAME_TIME_SEC" \
        --CharacterNum "$CHARACTER_NUM" \
        --fileName "$PLAYBACK_FILE" \
        --loglevel "$LOG_LEVEL"
fi

# ──────────────────────────────────────────────
# CLIENT
# ──────────────────────────────────────────────
if [ "$TERMINAL" = "CLIENT" ]; then
    echo "========================================="
    echo "  THUAI9 Client"
    echo "  Team:     $TEAM_ID"
    echo "  Server:   $SERVER_IP:$SERVER_PORT"
    echo "  Language: $PLAYER_LANG"
    echo "========================================="

    # Wait until server is reachable
    echo "[client] Waiting for server $SERVER_IP:$SERVER_PORT ..."
    while ! timeout 2 bash -c "echo >/dev/tcp/$SERVER_IP/$SERVER_PORT" 2>/dev/null; do
        sleep 2
        echo "[client] Still waiting..."
    done
    echo "[client] Server is reachable. Starting Team process..."

    if [ "$PLAYER_LANG" = "cpp" ]; then
        CAPI_BIN="$PLAYER_DIR/CAPI/cpp/install/bin/capi"
        if [ ! -x "$CAPI_BIN" ]; then
            CAPI_BIN="$PLAYER_DIR/CAPI/cpp/build/capi"
        fi
        if [ ! -x "$CAPI_BIN" ]; then
            CAPI_BIN="$(command -v capi || true)"
        fi
        if [ -z "$CAPI_BIN" ] || [ ! -x "$CAPI_BIN" ]; then
            echo "ERROR: C++ CAPI binary not found." >&2
            exit 1
        fi
        echo "[client] Running: $CAPI_BIN -t $TEAM_ID -p 0 -I $SERVER_IP -P $SERVER_PORT"
        exec "$CAPI_BIN" -t "$TEAM_ID" -p 0 -I "$SERVER_IP" -P "$SERVER_PORT"

    elif [ "$PLAYER_LANG" = "python" ]; then
        cd "$PLAYER_DIR/CAPI/python"
        export PYTHONPATH="$PLAYER_DIR/CAPI/python:$PLAYER_DIR/dependency/proto:${PYTHONPATH:-}"
        echo "[client] Running: python -m PyAPI.main -t $TEAM_ID -p 0 -I $SERVER_IP -P $SERVER_PORT --aiModule $AI_MODULE"
        exec python3 -m PyAPI.main \
            -t "$TEAM_ID" -p 0 \
            -I "$SERVER_IP" -P "$SERVER_PORT" \
            --aiModule "$AI_MODULE"

    else
        echo "ERROR: Unknown PLAYER_LANG=$PLAYER_LANG. Use 'cpp' or 'python'." >&2
        exit 1
    fi
fi

echo "ERROR: Unknown TERMINAL=$TERMINAL. Use 'SERVER' or 'CLIENT'." >&2
exit 1

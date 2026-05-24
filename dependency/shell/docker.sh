#!/usr/bin/env bash
# =============================================================================
# File         : dependency/shell/docker.sh
# Description  : Docker wrapper — compile or run THUAI9 containers
#
# Usage:
#   bash docker.sh -c              Compile C++ player code
#   bash docker.sh -r              Run server + client
#
# Examples:
#   bash docker.sh -c              # compiles code/ → output/
#   SERVER_PORT=9000 bash docker.sh -r
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
DEPENDENCY_DIR="$(dirname "$SCRIPT_DIR")"
ROOT_DIR="$(dirname "$DEPENDENCY_DIR")"

usage() {
    echo "Usage: bash docker.sh [-c] [-r]"
    echo "  -c   Compile C++ player code from code/ directory"
    echo "  -r   Run server + client"
    exit 1
}

while getopts ':cr' OPT; do
    case ${OPT} in
        c)
            echo "=== Compiling C++ player code ==="
            mkdir -p "$ROOT_DIR/code" "$ROOT_DIR/output"
            docker run --rm \
                -v "$ROOT_DIR/code:/usr/local/code" \
                -v "$ROOT_DIR/output:/usr/local/output" \
                eesast/thuai9_cpp:latest
            echo "=== Done. Binary in output/ ==="
            exit
            ;;
        r)
            echo "=== Starting THUAI9 Server ==="
            docker run --rm -d --name thuai9_server \
                -e TERMINAL=SERVER \
                -e SERVER_PORT="${SERVER_PORT:-8888}" \
                -e TEAM_COUNT="${TEAM_COUNT:-4}" \
                -e GAME_TIME="${GAME_TIME:-600}" \
                -p "${SERVER_PORT:-8888}:${SERVER_PORT:-8888}" \
                eesast/thuai9_server:latest

            echo "=== Starting THUAI9 Client (Team 1) ==="
            docker run --rm \
                -e TERMINAL=CLIENT \
                -e TEAM_ID="${TEAM_ID:-1}" \
                -e SERVER_IP="${SERVER_IP:-host.docker.internal}" \
                -e SERVER_PORT="${SERVER_PORT:-8888}" \
                -e PLAYER_LANG="${PLAYER_LANG:-python}" \
                eesast/thuai9_client:latest
            exit
            ;;
        *)
            usage
            ;;
    esac
done

usage

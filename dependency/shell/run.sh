#!/usr/bin/env bash
# =============================================================================
# File         : dependency/shell/run.sh
# Description  : THUAI9 container entrypoint — starts server or client
#                Adapted from THUAI8's proven competition pattern
#
# Environment Variables (SERVER):
#   TERMINAL       SERVER
#   PORT           Server listen port (default: 8888)
#   TEAM_COUNT     Number of teams (default: 2)
#   CHARACTER_NUM  Max characters per team (default: 6)
#   GAME_TIME      Game duration in seconds (default: 600)
#   MODE           ARENA → mode 2, COMPETITION → mode 1 (default: COMPETITION)
#   MAP_ID         Map file name (loaded from /usr/local/map/$MAP_ID.txt)
#   EXPOSED        1 = allow spectator (default), 0 = disallow
#   SCORE_URL      URL for server to pull ladder scores
#   TOKEN          JWT token for web API auth
#   FINISH_URL     URL to POST results when server crashes
#
# Environment Variables (CLIENT):
#   TERMINAL       CLIENT
#   TEAM_LABEL     This team's label — player files are {label}0, {label}1, ...
#                  Files must be in /usr/local/code/
#   TEAM_SEQ_ID    This team's sequence index (0 or 1); passed as side flag
#   TEAM_LABELS    All team labels, colon-separated (not used, informational)
#   GAME_TIME      Game duration in seconds (default: 600)
#   CONNECT_IP     Server address (default: 172.17.0.1)
#   PORT           Server port (default: 8888)
# =============================================================================
# ── Directories ────────────────────────────────────────────────────────────
output_dir=/usr/local/output
map_dir=/usr/local/map
code_dir=/usr/local/code
python_dir=/usr/local/PlayerCode/CAPI/python
mkdir -p $output_dir

# ── Mode conversion ────────────────────────────────────────────────────────
if [ "${MODE:-COMPETITION}" = "ARENA" ]; then
    mode_num=2
else
    mode_num=1
fi

# ── Defaults ───────────────────────────────────────────────────────────────
: "${PORT:=8888}"
: "${TEAM_COUNT:=2}"
: "${CHARACTER_NUM:=6}"
: "${GAME_TIME:=600}"
: "${EXPOSED:=1}"
: "${TEAM_SEQ_ID:=0}"
: "${TEAM_LABEL:=TeamA}"
: "${CONNECT_IP:=172.17.0.1}"

# ── Retry helper ───────────────────────────────────────────────────────────
function retry_command {
    local command="$1"
    local max_attempts=5
    local attempt_num=1
    local sleep_seconds=10

    while [ $attempt_num -le $max_attempts ]; do
        echo "Attempt $attempt_num / $max_attempts to run command: $command"

        eval $command &
        local pid=$!

        sleep $sleep_seconds

        if kill -0 $pid 2>/dev/null; then
            echo "Failed to connect to server. Retrying..."
            ((attempt_num++))
        else
            echo "Connected to server successfully."
            return 0
        fi
    done

    echo "Failed to connect to server after $max_attempts attempts."
    return 1
}

# ═══════════════════════════════════════════════════════════════════════════
#  SERVER
# ═══════════════════════════════════════════════════════════════════════════
if [ "$TERMINAL" = "SERVER" ]; then
    map_path=$map_dir/${MAP_ID:-default}.txt
    echo "Starting THUAI9 SERVER..."

    server_args=(
        --port $PORT
        --teamCount $TEAM_COUNT
        --CharacterNum $CHARACTER_NUM
        --gameTimeInSecond $GAME_TIME
        --fileName $output_dir/playback
        --resultFileName $output_dir/result
        --startLockFile $output_dir/start.lock
        --mode $mode_num
        --loglevel 5
    )

    if [ -f "$map_path" ]; then
        server_args+=(--mapResource "$map_path")
    fi

    if [ -n "${SCORE_URL:-}" ]; then
        server_args+=(--url "$SCORE_URL")
    fi

    if [ -n "${TOKEN:-}" ]; then
        server_args+=(--token "$TOKEN")
    fi

    if [ "$EXPOSED" -eq 0 ]; then
        server_args+=(--notAllowSpectator)
    fi

    dotnet /usr/local/Server/Server.dll "${server_args[@]}" > $output_dir/server.log 2>&1 &
    server_pid=$!
    echo "Server PID: $server_pid"
    ls $output_dir

    echo "SCORE URL: ${SCORE_URL:-none}"
    echo "FINISH URL: ${FINISH_URL:-none}"

    echo "Waiting for game to start..."
    sleep 60
    echo "Checking for start lock file..."

    if [ ! -f $output_dir/start.lock ]; then
        echo "Failed to start game (crashed before start lock)."
        touch temp.lock
        mv -f temp.lock $output_dir/playback.thuaipb
        kill -9 $server_pid 2>/dev/null || true

        if [ -n "${FINISH_URL:-}" ] && [ -n "${TOKEN:-}" ]; then
            finish_payload='{"status":"Crashed","scores":[0,0]}'
            curl "$FINISH_URL" -X POST \
                -H "Content-Type: application/json" \
                -H "Authorization: Bearer $TOKEN" \
                -d "$finish_payload" > $output_dir/send.log 2>&1 || true
        fi
    else
        echo "Game started successfully."
        while kill -0 $server_pid 2>/dev/null; do
            sleep 1
        done
        echo "Server exited normally."
    fi

# ═══════════════════════════════════════════════════════════════════════════
#  CLIENT
# ═══════════════════════════════════════════════════════════════════════════
elif [ "$TERMINAL" = "CLIENT" ]; then
    echo "Starting THUAI9 CLIENT for team $TEAM_LABEL (seq: $TEAM_SEQ_ID)"

    mkdir -p $output_dir
    team_id=$((TEAM_SEQ_ID + 1))

    pushd $code_dir
    player_idx=0
    while true; do
        code_name="${TEAM_LABEL}${player_idx}"

        if [ -f "./$code_name.py" ]; then
            echo "Found ./$code_name.py"

            cp_dir="${python_dir}${player_idx}"
            cp -r "$python_dir" "$cp_dir"
            cp -f "./$code_name.py" "$cp_dir/PyAPI/AI.py"

            command="nice -n 0 python3 ${cp_dir}/PyAPI/main.py \
                -I $CONNECT_IP -P $PORT \
                -t $team_id -s $TEAM_SEQ_ID \
                -p $player_idx"

            retry_command "$command" > "$output_dir/team${TEAM_SEQ_ID}-$code_name.log" 2>&1 &

        elif [ -f "./$code_name" ]; then
            echo "Found ./$code_name"

            command="nice -n 0 ./$code_name \
                -I $CONNECT_IP -P $PORT \
                -t $team_id -s $TEAM_SEQ_ID \
                -p $player_idx"

            retry_command "$command" > "$output_dir/team${TEAM_SEQ_ID}-$code_name.log" 2>&1 &

        else
            break
        fi

        ((player_idx++))
    done

    if [ $player_idx -eq 0 ]; then
        echo "ERROR: No player code files found in $code_dir for $TEAM_LABEL"
        popd
        exit 1
    fi

    echo "All $player_idx player(s) started for team $TEAM_LABEL."
    sleep $((GAME_TIME + 90))
    popd

else
    echo "ERROR: TERMINAL must be SERVER or CLIENT." >&2
    exit 1
fi

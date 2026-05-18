#!/usr/bin/env bash
# =============================================================================
# File         : dependency/shell/compile.sh
# Description  : Compile player C++ code (THUAI9 CAPI)
#
# Usage:
#   docker run -v "$(pwd)/code:/usr/local/code" -v "$(pwd)/output:/usr/local/output" \
#       eesast/thuai9_cpp:latest bash ./compile.sh
#
#   Or as ENTRYPOINT:
#       docker run -v ... eesast/thuai9_cpp:latest
#
# Environment variables:
#   URL           Curl callback URL (optional)
#   TOKEN         Auth token for callback (optional)
# =============================================================================
set -euo pipefail

WORKDIR="${WORKDIR:-/usr/local/PlayerCode/CAPI/cpp}"
BIND="${BIND:-/usr/local/code}"
OUTPUT="${OUTPUT:-/usr/local/output}"
URL="${URL:-}"
TOKEN="${TOKEN:-}"

flag=1

mkdir -p "$OUTPUT"

cd "$BIND"
file_count=$(ls -1 *.cpp 2>/dev/null | wc -l)

if [ "$file_count" -eq 1 ]; then
    filename=$(ls *.cpp)
    base_name=$(basename "$filename" .cpp)

    echo "[compile] Found source: $filename"
    echo "[compile] Target: $base_name"

    cd "$WORKDIR"
    cp -f "$BIND/$filename" "$WORKDIR/API/src/AI.cpp"

    echo "[compile] Running CMake..."
    (
        cmake -S . -B build \
            -DCMAKE_BUILD_TYPE=Release \
            -DCMAKE_CXX_STANDARD=17
        cmake --build build --config Release --parallel "$(nproc)"
    ) > "$OUTPUT/${base_name}.log" 2>&1

    # Copy binary
    if cp build/capi "$OUTPUT/$base_name" 2>/dev/null || \
       cp build/Release/capi "$OUTPUT/$base_name" 2>/dev/null; then
        echo "[compile] Success: $OUTPUT/$base_name"
    else
        echo "[compile] ERROR: binary not found after build" >&2
        flag=0
    fi

    cp "$OUTPUT/${base_name}.log" "$OUTPUT/${base_name}.log" 2>/dev/null || true
else
    echo "[compile] ERROR: expected exactly 1 .cpp file in $BIND, found $file_count" >&2
    flag=0
fi

if [ -n "$URL" ]; then
    if [ "$flag" -eq 1 ]; then
        curl "$URL" -X POST -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN" \
            -d '{"compile_status":"Completed"}' > "$OUTPUT/${base_name}.curl.log" 2>&1 || true
    else
        curl "$URL" -X POST -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN" \
            -d '{"compile_status":"Failed"}' > "$OUTPUT/${base_name}.curl.log" 2>&1 || true
    fi
fi

exit $(( 1 - flag ))

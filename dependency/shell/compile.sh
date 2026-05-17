#!/usr/bin/env bash
# =============================================================================
# File         : dependency/shell/compile.sh
# Description  : Compile C++ player code via CMake (THUAI9 CAPI)
# Usage        : bash compile.sh
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
CAPI_DIR="$SCRIPT_DIR/../../CAPI/cpp"
BUILD_DIR="$CAPI_DIR/build"
INSTALL_DIR="$CAPI_DIR/install"

echo "[compile] Regenerating C++ proto stubs..."
bash "$SCRIPT_DIR/cpp_output.sh"

echo "[compile] Configuring CMake..."
mkdir -p "$BUILD_DIR"
cmake -S "$CAPI_DIR" -B "$BUILD_DIR" \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_INSTALL_PREFIX="$INSTALL_DIR" \
    -DCMAKE_CXX_STANDARD=17

echo "[compile] Building..."
cmake --build "$BUILD_DIR" --config Release --parallel "$(nproc)"

echo "[compile] Installing binary..."
mkdir -p "$INSTALL_DIR/bin"
cp "$BUILD_DIR/capi" "$INSTALL_DIR/bin/capi" 2>/dev/null || \
    cp "$BUILD_DIR/Release/capi" "$INSTALL_DIR/bin/capi" 2>/dev/null || \
    cp "$BUILD_DIR/capi" "$INSTALL_DIR/bin/capi" 2>/dev/null || true

echo "[compile] Done. Binary at: $INSTALL_DIR/bin/capi"
ls -la "$INSTALL_DIR/bin/"

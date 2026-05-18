#!/usr/bin/env bash
# =============================================================================
# File         : dependency/shell/generate_proto.sh
# Description  : Generate Python protobuf + gRPC stubs from proto files
# Usage        : bash generate_proto.sh
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROTO_DIR="$SCRIPT_DIR/../proto"
OUTPUT_DIR="$SCRIPT_DIR/../../CAPI/python/proto"
REQUIREMENTS="$SCRIPT_DIR/../../CAPI/python/requirements.txt"

echo "[generate_proto] Installing Python dependencies..."
python3 -m pip install --quiet -r "$REQUIREMENTS"

echo "[generate_proto] Generating Python stubs..."
mkdir -p "$OUTPUT_DIR"
: > "$OUTPUT_DIR/__init__.py"

python3 -m grpc_tools.protoc \
    -I"$PROTO_DIR" \
    --python_out="$OUTPUT_DIR" --pyi_out="$OUTPUT_DIR" \
    MessageType.proto Message2Clients.proto Message2Server.proto

python3 -m grpc_tools.protoc \
    -I"$PROTO_DIR" \
    --python_out="$OUTPUT_DIR" --pyi_out="$OUTPUT_DIR" \
    --grpc_python_out="$OUTPUT_DIR" \
    Services.proto

echo "[generate_proto] Done. Generated:"
ls -la "$OUTPUT_DIR"/*.py 2>/dev/null || true

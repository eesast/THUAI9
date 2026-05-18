#!/usr/bin/env bash
# =============================================================================
# File         : dependency/shell/cpp_output.sh
# Description  : Generate C++ protobuf + gRPC stubs from proto files (Linux)
# Usage        : bash cpp_output.sh
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROTO_DIR="$SCRIPT_DIR/../proto"
OUTPUT_DIR="$SCRIPT_DIR/../../CAPI/cpp/proto"

PROTOC_BIN="$(command -v protoc || true)"
GRPC_CPP_PLUGIN="$(command -v grpc_cpp_plugin || true)"

if [ -z "$PROTOC_BIN" ]; then
    echo "ERROR: protoc not found in PATH. Please install protobuf-compiler." >&2
    exit 1
fi
if [ -z "$GRPC_CPP_PLUGIN" ]; then
    echo "ERROR: grpc_cpp_plugin not found in PATH. Please install grpc." >&2
    exit 1
fi

echo "[cpp_output] Using protoc: $PROTOC_BIN"
echo "[cpp_output] Using grpc_cpp_plugin: $GRPC_CPP_PLUGIN"
echo "[cpp_output] Proto dir: $PROTO_DIR"
echo "[cpp_output] Output dir: $OUTPUT_DIR"

mkdir -p "$OUTPUT_DIR"

pushd "$PROTO_DIR" > /dev/null

echo "[cpp_output] Generating C++ files..."
"$PROTOC_BIN" Message2Clients.proto --cpp_out="$OUTPUT_DIR"
"$PROTOC_BIN" MessageType.proto       --cpp_out="$OUTPUT_DIR"
"$PROTOC_BIN" Message2Server.proto    --cpp_out="$OUTPUT_DIR"
"$PROTOC_BIN" Services.proto          --cpp_out="$OUTPUT_DIR" \
    --grpc_out="$OUTPUT_DIR" \
    --plugin=protoc-gen-grpc="$GRPC_CPP_PLUGIN"

popd > /dev/null

echo "[cpp_output] Done. Generated:"
ls -la "$OUTPUT_DIR"/*.pb.h "$OUTPUT_DIR"/*.pb.cc "$OUTPUT_DIR"/*.grpc.pb.h "$OUTPUT_DIR"/*.grpc.pb.cc 2>/dev/null || true

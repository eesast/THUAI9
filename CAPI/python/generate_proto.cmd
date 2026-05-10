@echo off
setlocal

python -m pip install -r requirements.txt || exit /b 1

if not exist proto mkdir proto
type nul > proto\__init__.py

python -m grpc_tools.protoc -I../../dependency/proto --python_out=./proto --pyi_out=./proto MessageType.proto || exit /b 1
python -m grpc_tools.protoc -I../../dependency/proto --python_out=./proto --pyi_out=./proto Message2Clients.proto || exit /b 1
python -m grpc_tools.protoc -I../../dependency/proto --python_out=./proto --pyi_out=./proto Message2Server.proto || exit /b 1
python -m grpc_tools.protoc -I../../dependency/proto --python_out=./proto --pyi_out=./proto --grpc_python_out=./proto Services.proto || exit /b 1

endlocal

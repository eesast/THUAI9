# PyAPI

THUAI9 的 Python 选手接口实现。

主要文件：

- `main.py`：命令行入口
- `AI.py`：选手 AI 模板
- `API.py`：公开接口实现
- `DebugAPI.py`：调试版接口
- `logic.py`：状态同步与 AI 主循环
- `Communication.py`：gRPC 通信层
- `structures.py`：THUAI9 数据结构与枚举
- `utils.py`：视野判定、Proto 映射与消息构造

使用前先在 `CAPI/python` 目录运行 `generate_proto.cmd` 或 `generate_proto.sh` 生成 gRPC Python 代码。

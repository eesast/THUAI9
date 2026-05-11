# THUAI9 C++ 选手包 — 环境配置与运行指南

## 你需要什么

| 工具 | 说明 |
|------|------|
| **Visual Studio 2022** | 免费 Community 版即可，安装时勾选"使用 C++ 的桌面开发" |
| **vcpkg** | C++ 包管理器，后面会教你装 |
| **磁盘空间** | C 盘至少 **15 GB**（gRPC 编译需要） |

---

## 第一步：安装 vcpkg

打开 PowerShell，克隆到任意目录（建议不在 C 盘，如 `D:\vcpkg`）：

```powershell
git clone https://github.com/microsoft/vcpkg.git
cd vcpkg
.\bootstrap-vcpkg.bat
```

记下 vcpkg 的路径，后面用 `<vcpkg>` 表示。

---

## 第二步：安装依赖

进入选手包目录，执行一条命令：

```powershell
cd CAPI\cpp
<vcpkg>\vcpkg.exe install --triplet=x64-windows
```

这会根据项目里的 `vcpkg.json` 自动安装所需依赖（gRPC + protobuf 及其关联库），**从源码编译约需 30-60 分钟**。如果中途报错 `no space on device`，说明磁盘满了，清理后重试。

---

## 第三步：注册到 Visual Studio

```powershell
<vcpkg>\vcpkg.exe integrate install
```

看到 `Applied user-wide integration` 就是成功了。这一步让 VS 自动找到刚装的库，**以后不用再跑**。

---

## 第四步：编译

1. 双击 `CAPI\cpp\API.sln` → 用 Visual Studio 2022 打开
2. 顶部选 **x64** + **Debug**
3. 菜单 → 生成 → 生成解决方案（快捷键 `Ctrl+Shift+B`）
4. 输出窗口显示"生成: 1 成功"即完成

编译产物在 `CAPI\cpp\x64\Debug\API.exe`。

---

## 第五步：运行

CAPI 是客户端，需要**先启动服务端**。

**终端 1 — 启动服务端：**
```powershell
cd logic\Server
dotnet run -- --port 8888 --gameTimeInSecond 120 --teamCount 2
```

**终端 2 — 启动选手 AI：**
```powershell
CAPI\cpp\x64\Debug\API.exe -t 0 -p 0 -I 127.0.0.1 -P 8888
```

### 命令行参数

| 参数 | 含义 | 示例 |
|------|------|------|
| `-t` | 队伍编号（从 0 开始） | `-t 0` |
| `-p` | 玩家编号（从 0 开始） | `-p 0` |
| `-I` | 服务端 IP 地址 | `-I 127.0.0.1` |
| `-P` | 服务端端口 | `-P 8888` |
| `-d` | 启用 Debug 日志（可选） | `-d` |

---

## 写 AI 代码

只需改一个文件：**`CAPI\cpp\API\src\AI.cpp`**

里面有两个函数：

```cpp
void AI::play(ICharacterAPI& api)  // 每个角色每帧调用一次
void AI::play(ITeamAPI& api)       // 队伍级每帧调用一次
```

已有完整参考实现（BFS 寻路 + 角色状态机 + 战斗），可直接在此基础上修改。

API 能做什么：`Move()`、`Attack()`、`Harvest()`、`Occupy()`、`Load()`、`Sell()`、`Buy()`、`BuildCharacter()`、`ProduceGoods()`、`UplevelTech()`、`SendMessage()` 等。详见 `API\include\API.h`。

---

## 常见问题

**Q: 编译报"无法打开 xxx.lib"**  
A: 确认已执行第三步 `vcpkg integrate install`，然后重启 VS。

**Q: vcpkg install 到一半报错**  
A: 最常见的原因是磁盘不足（gRPC 编译占约 3-5 GB）。清理磁盘后重新运行安装命令。

**Q: 启动时连接失败**  
A: 确认服务端已启动，IP 和端口正确。

**Q: 能用 Win32 配置吗**  
A: 不行，用 **x64**。

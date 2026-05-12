# THUAI9 C++ 选手运行说明

## 1. 修改 AI

只需要修改 `CAPI/cpp/API/src/AI.cpp`。

## 2. 用 VS2022 编译

1. 打开 `CAPI/cpp/API.sln`
2. 选择 `x64` 平台
3. 在 Visual Studio 2022 中编译生成 `API.exe`

## 2.1 示例程序说明

- `AI_harvest.cpp`：采集示例，会自动找最近资源，移动到资源旁采集，再回到本队工厂；队伍接口里还会先建一个生产角色，并周期性生产 `Food`。
- 这个文件都是给选手参考的示例程序，不是比赛必需代码。

## 3. 启动图形化配置界面

编译完成后，回到仓库根目录，双击运行：

```
start_thuai9_cpp_gui.bat
```

会弹出配置窗口，可以设置：

- 服务器端口、队伍数（1–4）、每队角色数、游戏时长
- CAPI 调试选项（日志、控制台输出）
- 每支队伍单独指定 `API.exe`（留空则自动使用编译产物）
- 回放文件名、是否启动 UI

配置完成后点击 **Start Game**，脚本会自动启动 UI、服务器和各队 CAPI 进程。

## 4. 注意事项

- "Team CAPI Executables" 中留空的队伍，会自动使用 `CAPI\cpp\x64\Debug\API.exe` 或 `Release\API.exe`（Debug 优先）
- 每支队伍可以单独点击 `...` 按钮选择不同的 `API.exe`，方便多队对战
- 配置会保存到 `thuai9_launch_config.json`，下次打开自动还原

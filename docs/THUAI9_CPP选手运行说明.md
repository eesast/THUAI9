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

## 3. 启动一键运行脚本

编译完成后，回到仓库根目录，双击运行：

单队伍测试脚本： `start_thuai9_cpp_1teamdebugversion.bat`

多队伍运行脚本：`start_thuai9_cpp_4teamversion.bat`

脚本会自动启动 UI、服务器和 CAPI 进程。

## 4. 注意事项

- 如果编译的是 `Debug`，脚本会优先使用 `CAPI\cpp\x64\Debug\API.exe`
- 如果编译的是 `Release`，脚本会自动改用 `CAPI\cpp\x64\Release\API.exe`
- 如果你想跑四队版本，可以改用 `start_thuai9_cpp_4teamversion.bat`

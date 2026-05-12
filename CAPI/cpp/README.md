# CAPI: cpp

## 简介

这是 THUAI9 的 C++ 选手接口工程。

选手使用方式：

1. 安装 `Visual Studio 2022`
2. 安装工作负载 `Desktop development with C++`
3. 打开 [API.sln](./API.sln)
4. 选择 `x64`
5. 直接编译 `Debug` 或 `Release`

当前仓库已经内置 C++ API 所需的第三方头文件和预编译库，选手**不需要**再安装或配置：

- `vcpkg`
- `gRPC`
- `protobuf`
- `absl`
- `re2`
- `OpenSSL`
- `zlib`

## 环境要求

- `Visual Studio 2022`
- `MSVC v143` 工具链
- `Windows SDK`

说明：

- 工程当前支持并验证通过的配置是 `x64 Debug` 和 `x64 Release`
- 不要求配置 `VcpkgRoot`
- 不依赖本机额外安装的第三方 C++ 库

## 目录说明

- [API.sln](./API.sln): 选手直接打开的解决方案
- [API/API.vcxproj](./API/API.vcxproj): VS 工程文件
- [API/include](./API/include): C++ 接口头文件
- [API/src](./API/src): C++ 接口实现
- [proto](./proto): 已生成的 protobuf / gRPC 源码
- [grpc/include](./grpc/include): 工程随包分发的第三头文件
- [lib/debug](./lib/debug): `x64 Debug` 依赖库
- [lib/release](./lib/release): `x64 Release` 依赖库

## 编译说明

### Visual Studio

1. 打开 [API.sln](./API.sln)
2. 在顶部配置中选择：
   - `Debug` 或 `Release`
   - `x64`
3. 点击“生成解决方案”

生成产物默认位于：

- `x64/Debug/API.exe`
- `x64/Release/API.exe`

### MSBuild

如果需要命令行编译，可使用：

```powershell
MSBuild.exe API.sln /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /m
MSBuild.exe API.sln /t:Rebuild /p:Configuration=Release /p:Platform=x64 /m
```

## 当前验证结果

本工程已验证：

- 在不设置 `VcpkgRoot` 的情况下可编译
- 将 `CAPI/cpp` 单独复制到新目录后仍可编译
- `x64 Debug` 可生成成功
- `x64 Release` 可生成成功

## 常见问题

### 1. 打开工程后无法编译

先确认本机是否安装了：

- `Visual Studio 2022`
- `Desktop development with C++`
- `Windows SDK`

### 2. 需要自己安装 vcpkg 吗

不需要。

### 3. 需要自己生成 protobuf / gRPC 代码吗

不需要，仓库中已经包含生成后的源码。

### 4. 需要自己准备 `.lib` 文件吗

不需要，仓库中已经包含 `Debug` 和 `Release` 所需库文件。

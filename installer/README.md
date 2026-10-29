# THUAI9 下载器

基于 Avalonia 的 Windows 桌面下载器，通过云函数获取选手包文件列表和下载链接，将云端文件同步到本地。

## 选手使用

从 GitHub Actions 的 `THUAI9-downloader-win-x64` 构建产物中获取 `CosDownloader.exe`，解压后运行。发布版面向 Windows x64，内置 .NET 运行时，无需安装 .NET SDK。

1. 点击刷新按钮，获取云端文件列表。
2. 默认下载位置为桌面的 `CosDownloads` 文件夹，也可以选择其他位置。选择父目录时，程序会自动创建 `CosDownloads` 子目录。
3. 开始下载，程序会跳过未变更的文件并下载新增或更新的文件。
4. 如需保留自己的 AI 等本地修改，先在文件树中选中对应文件或目录并启用保护；受保护项目会跳过同步，也可以取消保护。

暂停会保留已完成的文件。继续同步时，会重新下载暂停时尚未完成的那个文件；当前版本不支持单文件按字节断点续传。

下载器负责文件下载和同步，启动对局请使用选手包中的启动脚本。

## 开发环境与运行

安装 .NET 10 SDK。在仓库根目录执行：

```powershell
dotnet restore ./installer/installer.sln
dotnet build ./installer/installer.sln -c Release --no-restore
dotnet run --project ./installer/installer/installer.csproj
```

也可以用支持 .NET 10 的 IDE 打开 `installer.sln`。

## 发布 Windows 单文件版

在仓库根目录执行：

```powershell
dotnet publish ./installer/installer/installer.csproj -p:PublishProfile=Windows
```

输出文件为：

```text
installer/installer/bin/Release/net10.0/win-x64/publish/CosDownloader.exe
```

发布配置位于 [Windows.pubxml](installer/Properties/PublishProfiles/Windows.pubxml)，使用自包含、压缩的单文件发布，并将原生依赖一同打包。构建工作流也使用同一配置发布并保存 EXE 构建产物。

## 源码结构

| 文件 | 用途 |
|---|---|
| `Program.cs`、`App.axaml*` | 桌面应用入口、主题和主窗口初始化 |
| `MainWindow.axaml*` | 文件列表、下载进度、目录选择、暂停继续和保护操作 |
| `CosService.cs` | 文件列表请求、签名链接、下载和增量同步 |
| `ExplorerNode.cs` | 文件树节点与目录统计 |
| `SyncManifest.cs` | 本地同步清单 |
| `SyncProtection.cs` | 文件和目录保护设置 |
| `CloudFunctionConfig.cs` | 云函数地址和接口路径 |
| `Assets/`、`app.manifest` | 应用图标与 Windows 清单 |

## 云函数与本地数据

客户端请求 `GET /files` 获取文件列表；缺少下载链接时，再请求 `GET /sign?key=...` 获取签名下载地址。部署其他服务时修改 [CloudFunctionConfig.cs](installer/CloudFunctionConfig.cs)，接口约定见 [CloudFunctionApiContract.md](installer/CloudFunctionApiContract.md)。COS 访问凭据由服务端管理，客户端不需要配置。

同步清单和保护设置保存在以下目录，并按下载根目录分别记录：

- `%LOCALAPPDATA%/THUAI9Downloader/manifests/`
- `%LOCALAPPDATA%/THUAI9Downloader/protections/`

程序仍兼容读取下载目录中旧版的 `.thuai9-manifest.json`。

提交源码时不要包含 `bin/`、`obj/`、PDB 或本地同步数据。发布 EXE 使用构建产物或 Release 附件分发。

## 开发人员

- 李科、李果

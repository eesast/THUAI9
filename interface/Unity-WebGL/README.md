# THUAI9 Unity WebGL 启动说明

本目录用于放置三套互相解耦的 WebGL 发布产物：

- `trial/`：本地试玩
- `live/`：直播观战
- `playback/`：回放播放

## 本地启动

从仓库根目录执行时，先进入 `interface/Unity-WebGL`，再启动本地网页服务：

```powershell
Set-Location .\interface\Unity-WebGL
python -m http.server 18089 --bind 127.0.0.1
```

注意：必须从 `interface/Unity-WebGL` 启动，不要只复制第二行裸命令。`http://127.0.0.1:18089/` 只能显示 `live`、`playback`、`trial` 三个入口；如果看到 `Directory listing for /` 或 `.git`、`logic`、`tasks` 等仓库内容，说明服务启动目录错了，请先停掉旧服务后重新在本目录启动。

然后打开：

```text
http://127.0.0.1:18089/
http://127.0.0.1:18089/trial/index.html
http://127.0.0.1:18089/live/index.html
http://127.0.0.1:18089/playback/index.html
```

## 参数

- 直播：`/live/index.html?ws=ws://127.0.0.1:xxxx/live`
- 回放：`/playback/index.html?url=http://.../xxx.thuaipb`
- 试玩：`/trial/index.html` 会自动启动本地试玩。

不要直接双击 HTML 文件，请使用本地 HTTP 服务启动。

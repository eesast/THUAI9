# THUAI9 Unity WebGL 启动说明

本目录用于放置三套互相解耦的 WebGL 发布产物：

- `trial/`：本地试玩
- `live/`：直播观战
- `playback/`：回放播放

## 本地启动

先进入仓库里的 `interface/Unity-WebGL` 目录，再启动本地网页服务：

```powershell
python -m http.server 18089 --bind 127.0.0.1
```

然后打开：

```text
http://127.0.0.1:18089/trial/index.html
http://127.0.0.1:18089/live/index.html
http://127.0.0.1:18089/playback/index.html
```

## 参数

- 直播：`/live/index.html?ws=ws://127.0.0.1:xxxx/live`
- 回放：`/playback/index.html?url=http://.../xxx.thuaipb`
- 试玩：`/trial/index.html` 会自动启动本地试玩。

不要直接双击 HTML 文件，请使用本地 HTTP 服务启动。

# THUAI9 Web 启动说明

## 启动方法

1. 确认电脑已安装 Python 3。
2. 打开 PowerShell，进入本目录：

```powershell
cd D:\MyPro\THUAI\THUAI9\interface\Unity-WebGL
```

3. 启动本地网页服务：

```powershell
python -m http.server 18089 --bind 127.0.0.1
```

4. 在浏览器打开：

```text
http://127.0.0.1:18089/
```

5. 等待 Unity 页面加载完成后即可使用。

## 注意事项

- 不要直接双击 `index.html` 打开，请使用上面的本地网页服务。
- 如果 `18089` 端口被占用，可以换成其他端口，例如：

```powershell
python -m http.server 18090 --bind 127.0.0.1
```

然后打开：

```text
http://127.0.0.1:18090/
```

- 停止网页服务：回到 PowerShell 窗口，按 `Ctrl + C`。

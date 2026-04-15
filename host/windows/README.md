# Windows 11 Remote Host

.NET 8 控制台应用：WebSocket 信令、WebRTC（SIPSorcery + `WindowsVideoEndPoint`，**VP8** 视频）、可选 DXGI 桌面采集（BGRA→同一 VP8 管线）、`SendInput` 输入注入、本地探针 HTTP。计划中的 H.264 可作为后续编码器扩展。

## 控制消息（DataChannel，JSON）

协议前缀：`ctrl:v1`（字段 `v`=1）。

| `t` | 字段 | 说明 |
|-----|------|------|
| `move` | `x`,`y` 归一化 0..1 | 鼠标移动到主显示器虚拟坐标 |
| `down` / `up` | `x`,`y`, `b` 0=左 1=右 2=中 | 鼠标按下/抬起 |
| `wheel` | `dx`,`dy` | 滚轮 |
| `key` | `k` 虚拟键码 | 键盘按下/抬起见 `down` 布尔 |
| `text` | `s` | Unicode 文本输入 |

示例：

```json
{"v":1,"t":"move","x":0.5,"y":0.5}
{"v":1,"t":"down","x":0.5,"y":0.5,"b":0}
{"v":1,"t":"up","x":0.5,"y":0.5,"b":0}
{"v":1,"t":"key","k":13,"down":true}
{"v":1,"t":"text","s":"dir"}
```

## 运行

```powershell
cd host\windows\src\RemoteHost
dotnet run -- --signaling ws://127.0.0.1:8787 --room demo --role host --probe 18080 --video test
dotnet run -- --signaling ws://127.0.0.1:8787 --room demo --role host --probe 18080 --video desktop
```

环境变量 `ICE_SERVERS`：分号分隔，例如 `stun:stun.l.google.com:19302;turn:turn.example.com:3478|user|pass`。

## 探针

`GET http://127.0.0.1:<probe>/health`  
`GET http://127.0.0.1:<probe>/stats` — `connectionState`, `iceState`, `clicks`, `keys`, `lastError`.

仅绑定 `127.0.0.1`，供 E2E 断言。

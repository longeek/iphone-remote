# iphone-remote — Android 远程查看/控制 Windows 11

混合架构：Windows 主机使用 .NET + WebRTC（SIPSorcery）进行桌面采集、编码与输入注入；Android 使用 Kotlin + Google WebRTC；信令为 Node.js WebSocket；公网场景配合 STUN/TURN（见 `infra/`）。

## 仓库布局

| 目录 | 说明 |
|------|------|
| [docs/SIGNALING_PROTOCOL.md](docs/SIGNALING_PROTOCOL.md) | 信令消息版本与字段（与实现保持一致） |
| [server/signaling](server/signaling) | WebSocket 信令服务 |
| [host/windows](host/windows) | Windows 11 Agent（WebRTC、DXGI、输入、可选探针） |
| [apps/android](apps/android) | Android 客户端（Compose、会话、触控/键盘） |
| [infra](infra) | coturn 等部署示例 |
| [e2e](e2e) | Maestro 流程与集成脚本 |

## 快速开始（开发）

### 1. 信令服务

```bash
cd server/signaling
npm install
npm start
```

默认 `ws://0.0.0.0:8787`。

### 2. Windows Agent

```bash
cd host/windows/src/RemoteHost
dotnet run -- --signaling ws://127.0.0.1:8787 --room demo --role host --probe 18080
```

### 3. Android

用 Android Studio 打开 `apps/android`，配置 `local.properties` 与 `BuildConfig` 中的信令地址，运行 `app`。

## 协议版本

信令协议：**v1**（见 [docs/SIGNALING_PROTOCOL.md](docs/SIGNALING_PROTOCOL.md)）。

## TURN / 公网

示例：`infra/docker-compose.coturn.yml`。主机环境变量 `ICE_SERVERS` 使用 `|` 分隔多个条目，单条 TURN 凭证格式见 `RTCIceServer.Parse`（`turn:host:port;user;pass`）。

## 打包前测试（真实模拟 / E2E）

建议在打 **AAB/APK** 或发版前至少跑完下面几类检查；脚本一键入口：[e2e/scripts/run-packaging-gate.ps1](e2e/scripts/run-packaging-gate.ps1)。

### 1. 自动化门禁（无真机也可部分执行）

| 步骤 | 命令 / 说明 |
|------|-------------|
| 信令单元测试 | `cd server/signaling && npm test` |
| Windows 主机编译 | `dotnet build host/windows/src/RemoteHost/RemoteHost.csproj -c Release` |
| Android 编译（含 `androidTest`） | `cd apps/android && ./gradlew :app:assembleDebug :app:assembleDebugAndroidTest`（需先 `gradle wrapper` 若仓库无 `gradlew`） |

### 2. Android Instrumentation（模拟真实用户：登录 → 点连接 → 进入会话界面）

在 **已连接的设备或模拟器** 上：

```bash
cd apps/android
./gradlew :app:connectedDebugAndroidTest
```

测试类：[MainActivityInteractionTest.kt](apps/android/app/src/androidTest/java/com/iphoneremote/remote/MainActivityInteractionTest.kt)（校验带 `e2e_*` 的控件与进入会话后的 `SurfaceView` 区域；不要求信令/WebRTC 一定成功）。

### 3. Maestro（模拟手机端操作路径）

1. 安装 [Maestro](https://maestro.mobile.dev/)，安装 Debug APK：`./gradlew :app:installDebug`。
2. 主界面已启用 `testTagsAsResourceId`，流程里可用 `id: e2e_*` 选中控件。
3. 推荐流程：
   - [e2e/maestro/android-login-and-session.yaml](e2e/maestro/android-login-and-session.yaml) — 登录 → 连接 → 断言进入会话壳层；
   - [e2e/maestro/android-remote-touch-sim.yaml](e2e/maestro/android-remote-touch-sim.yaml) — 在会话页点击屏幕中部，模拟远程点击；
   - [e2e/maestro/android-smoke.yaml](e2e/maestro/android-smoke.yaml) — 轻量冒烟（文本断言）。

### 4. 全链路真实集成（可选）

在同一局域网：启动信令 → Windows `RemoteHost` → 手机 App 进同一 `room`，再用探针或肉眼确认画面与控制。

- Windows 探针：`RemoteHost --probe <port>` 后访问 `http://127.0.0.1:<port>/health` 与 `/stats`。

## 许可

按项目需要添加 LICENSE。

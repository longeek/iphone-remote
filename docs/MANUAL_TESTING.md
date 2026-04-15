# 人工测试指导说明

本文档提供在 Windows 和 Android 手机上进行人工测试的完整指导。适用于开发自测、QA 验收、客户演示等场景。

---

## 1. 测试环境准备

### 1.1 硬件要求

| 设备 | 最低配置 | 推荐配置 |
|------|----------|----------|
| Windows 电脑 | Windows 10/11，4GB RAM，双核 CPU | Windows 11，8GB RAM，4 核以上 CPU |
| Android 手机 | Android 7.0+，支持 WebRTC | Android 10+，4GB RAM |
| 网络 | 同一局域网，5GHz Wi-Fi 优先 | 有线网络 + 5GHz Wi-Fi |

### 1.2 软件依赖

| 软件 | 版本 | 安装说明 |
|------|------|----------|
| Node.js | 18+ | [nodejs.org](https://nodejs.org) |
| .NET SDK | 8.0+ | [dotnet.microsoft.com](https://dotnet.microsoft.com) |
| Android Studio | 2022+ | [developer.android.com](https://developer.android.com/studio) |
| ADB | 随 Android Studio | 确保 `adb` 在 PATH 中 |

### 1.3 网络要求

- **信令服务器**：可部署在本地或云服务器，需能被 Windows 和手机同时访问
- **端口**：8787（信令）、18080（可选探针）
- **防火墙**：确保 UDP/TCP 端口未被阻断

---

## 2. 服务启动

### 2.1 信令服务器

#### 本地启动（开发测试）

```bash
cd server/signaling
npm install
npm start
```

输出：`Signaling listening on ws://0.0.0.0:8787`

#### Docker 启动（生产环境）

```bash
cd infra
docker-compose up -d coturn
```

> 注意：Docker 方式仅启动 TURN，如需信令仍需 npm start。

### 2.2 Windows 主机

#### 编译运行

```bash
cd host/windows/src/RemoteHost
dotnet restore
dotnet run -- --signaling ws://127.0.0.1:8787 --room demo --probe 18080
```

#### 参数说明

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `--signaling` | `ws://127.0.0.1:8787` | 信令服务器地址 |
| `--room` | `default` | 房间 ID，手机端需保持一致 |
| `--probe` | 无 | 启用 HTTP 探针（示例：18080） |
| `--video` | `test` | 视频源：`test`（测试图案）或 `desktop`（真实桌面） |

#### 验证启动成功

```bash
# 探针方式（如果启用了 --probe）
curl http://127.0.0.1:18080/health
# 输出：ok

curl http://127.0.0.1:18080/stats
# 输出：{"connectionState":"connected","iceState":"new",...}
```

### 2.3 Android 客户端

#### 安装 APK

```bash
cd apps/android
./gradlew :app:installDebug
```

或在 Android Studio 中直接运行。

#### 配置信令地址

首次打开 App 时，在「Signaling WebSocket」字段填写信令服务器地址：

- **本地测试**：`ws://<电脑局域网IP>:8787`（手机通过 Wi-Fi 访问电脑）
- **云端测试**：`ws://<服务器IP>:8787`

#### 连接房间

在「Room ID」字段填写与 Windows 主机相同的房间名（如 `demo`），点击「Connect」。

---

## 3. 功能测试用例

### 3.1 连接与认证

#### 测试步骤

1. 启动信令服务器
2. 启动 Windows 主机，确认「connected」状态
3. 在手机上点击「Connect」
4. 观察连接状态变化

#### 预期结果

| 阶段 | Windows 主机日志 | Android 手机状态 |
|------|------------------|------------------|
| 手机发送 join | — | 「Connecting…」 |
| 服务器转发 peer | 「peer joined: client」 | — |
| WebRTC 连接 | 「ICE connected」「Video playing」 | 会话界面出现视频 |

#### 通过标准

- [ ] 手机成功进入会话界面
- [ ] Windows 主机显示「ICE connected」
- [ ] 双方都能看到对方在线（peer 事件）

### 3.2 断开连接

#### 测试步骤

1. 保持连接状态
2. 点击手机右上角的「断开」按钮（X 图标）
3. 观察返回登录界面

#### 预期结果

- [ ] 点击断开后，会话界面关闭
- [ ] 回到登录界面（输入框和连接按钮可见）
- [ ] Windows 主机收到 peer left 事件

### 3.3 会话界面元素验证

#### 测试步骤

1. 进入会话界面后，检查以下 UI 元素

#### 检查清单

- [ ] 视频渲染区域（SurfaceView）完整显示
- [ ] 状态栏显示当前连接状态（如「Video playing」）
- [ ] 右上角有「键盘」按钮（可展开输入面板）
- [ ] 右上角有「断开」按钮

---

## 4. 触控交互测试

### 4.1 单指点击（鼠标左键）

#### 测试步骤

1. 在手机会话界面，单指点击屏幕任意位置
2. 观察 Windows 端的鼠标移动和点击行为

#### 预期结果

- [ ] 单指点击触发鼠标左键按下+抬起事件
- [ ] 点击位置映射到屏幕相对坐标（0~1 归一化）

### 4.2 长按（鼠标右键）

#### 测试步骤

1. 在手机会话界面，长按屏幕任意位置（约 1 秒）
2. 观察 Windows 端的右键点击行为

#### 预期结果

- [ ] 长按触发鼠标右键按下+抬起事件
- [ ] 按钮值为 1（右键）

### 4.3 拖动（鼠标移动）

#### 测试步骤

1. 在手机会话界面，单指按住并滑动
2. 观察 Windows 端的鼠标移动轨迹

#### 预期结果

- [ ] 拖动过程中持续发送 move 事件
- [ ] 鼠标位置随手指移动平滑变化

### 4.4 双指滚动（滚轮）

#### 测试步骤

1. 在手机会话界面，双指同时触屏
2. 上下滑动双指

#### 预期结果

- [ ] 双指纵向滑动触发 wheel 事件
- [ ] 向上滑动对应正向滚动，向下滑动对应负向滚动

---

## 5. 键盘与文本测试

### 5.1 快捷键测试

#### 测试步骤

1. 进入会话界面
2. 点击右上角「键盘」按钮，打开输入面板
3. 点击面板中的快捷键按钮

#### 快捷键检查清单

| 按钮 | 预期 Windows 按键 |
|------|------------------|
| Esc | VK_ESCAPE (27) |
| Tab | VK_TAB (9) |
| Enter | VK_RETURN (13) |
| Bksp | VK_BACK (8) |
| Del | VK_DELETE (46) |
| ↑ | VK_UP (38) |
| ↓ | VK_DOWN (40) |
| ← | VK_LEFT (37) |
| → | VK_RIGHT (39) |
| F1~F12 | VK_F1 (112) ~ VK_F12 (123) |

- [ ] 每个快捷键按钮点击后，Windows 端收到对应的 key down + key up 事件

### 5.2 文本输入

#### 测试步骤

1. 打开键盘输入面板
2. 在文本框中输入英文字母
3. 在文本框中输入中文字符
4. 在文本框中输入特殊符号（如 `!@#$%`）

#### 预期结果

- [ ] 英文输入正常发送
- [ ] 中文输入正常发送
- [ ] 特殊符号正常发送

### 5.3 特殊字符测试

#### 测试步骤

1. 输入包含换行符的文本
2. 输入包含空格的文本
3. 输入 emoji 字符

#### 预期结果

- [ ] 换行符正确传递
- [ ] 空格正确传递
- [ ] emoji 字符正确传递（如可用）

---

## 6. 网络异常测试

### 6.1 Wi-Fi 断开重连

#### 测试步骤

1. 保持手机与 Windows 正常连接
2. 关闭手机的 Wi-Fi（飞行模式）
3. 等待 5~10 秒
4. 重新打开 Wi-Fi

#### 预期结果

- [ ] 断开期间手机显示「Connection lost」状态
- [ ] 重新连接后自动恢复会话
- [ ] 重连尝试次数不超过 5 次
- [ ] 重连失败后显示错误信息

### 6.2 网络切换

#### 测试步骤

1. 手机连接 Wi-Fi A，正常使用
2. 切换到 Wi-Fi B（IP 变化）
3. 观察连接状态

#### 预期结果

- [ ] 网络切换后连接断开
- [ ] 如信令服务器可达，自动触发重连

---

## 7. 边界场景测试

### 7.1 重复加入房间

#### 测试步骤

1. 启动 Windows 主机加入房间 `test`
2. 用手机 A 加入同一房间 `test`
3. 用手机 B 尝试加入同一房间 `test`

#### 预期结果

- [ ] 手机 B 收到错误：「client_taken」
- [ ] 手机 B 连接被断开

### 7.2 多房间并存

#### 测试步骤

1. 启动 Windows 主机加入房间 `room1`
2. 用手机 A 加入房间 `room1`
3. 启动第二个 Windows 主机实例加入房间 `room2`
4. 用手机 B 加入房间 `room2`

#### 预期结果

- [ ] room1 和 room2 相互隔离
- [ ] room1 的信令不会发送到 room2

### 7.3 主机离线后重连

#### 测试步骤

1. 保持手机与 Windows 正常连接
2. 强制关闭 Windows 主机进程
3. 等待 3 秒
4. 重新启动 Windows 主机，加入同一房间
5. 观察手机端是否收到新主机上线通知

#### 预期结果

- [ ] 原主机断开后，手机收到「peer left」
- [ ] 新主机加入后，手机收到「peer joined」
- [ ] 双方可重新建立 WebRTC 连接

---

## 8. 性能观察

### 8.1 延迟测试

#### 测试步骤

1. 建立正常连接
2. 在手机上快速连续点击
3. 观察 Windows 端响应时间

#### 观察指标

- [ ] 触控到鼠标事件延迟 < 100ms（局域网）
- [ ] 视频帧延迟 < 500ms

### 8.2 画质与帧率

#### 测试步骤

1. 观察 Windows 主机视频源（test 模式或 desktop）
2. 在手机端观察视频画质
3. 确认画面流畅无明显卡顿

#### 观察指标

- [ ] 视频分辨率至少 640x480
- [ ] 帧率至少 15fps
- [ ] 无明显花屏或马赛克

### 8.3 资源占用

#### 测试步骤

1. 在任务管理器中观察 Windows 主机 CPU 使用率
2. 观察手机端内存使用

#### 观察指标

- [ ] Windows 主机 CPU < 30%（test 模式）/ < 80%（desktop 模式）
- [ ] 手机内存使用无持续增长

---

## 9. 问题排查

### 9.1 常见错误

| 错误现象 | 可能原因 | 解决方法 |
|----------|----------|----------|
| 手机显示「Connection failed」 | 信令服务器未启动或 IP 错误 | 检查服务器地址，确保手机能 Ping 通电脑 |
| Windows 显示「ICE failed」 | STUN/TURN 服务器不可用 | 检查 ICE_SERVERS 环境变量，确保公网可访问 |
| 视频黑屏 | 防火墙阻断 UDP 端口 | 检查 Windows 防火墙规则 |
| 触控无反应 | DataChannel 未建立 | 确认 WebRTC 连接成功后再测试 |
| 重连失败 | 网络不稳定或超时 | 尝试手动断开后重新连接 |

### 9.2 诊断方法

#### 1. 查看信令服务器日志

```bash
cd server/signaling
npm start
# 观察日志中的 join、signal、peer 事件
```

#### 2. 查看 Windows 主机日志

```bash
cd host/windows/src/RemoteHost
dotnet run -- --signaling ws://... --room ... --probe 18080
# 观察 [webrtc]、[ctrl]、[ice] 等前缀的日志
```

#### 3. 使用探针检查状态

```bash
# 健康检查
curl http://127.0.0.1:18080/health

# 详细状态
curl http://127.0.0.1:18080/stats
```

#### 4. 使用 adb 查看手机日志

```bash
adb logcat -s MainActivity:D
# 过滤 MainActivity 的调试输出
```

#### 5. 检查网络连通性

```bash
# 从手机 Ping 电脑
adb shell ping -c 3 <电脑局域网IP>

# 检查端口可达性
adb shell nc -zv <电脑IP> 8787
```

---

## 附录：测试检查清单

### 基础功能

- [ ] 信令服务器正常启动
- [ ] Windows 主机正常启动并加入房间
- [ ] Android 客户端成功连接
- [ ] 视频流正常传输
- [ ] 断开连接功能正常
- [ ] 重连功能正常

### 触控交互

- [ ] 单指点击（左键）
- [ ] 长按（右键）
- [ ] 单指拖动（移动）
- [ ] 双指滚动（滚轮）

### 键盘输入

- [ ] 快捷键（Esc、Tab、Enter、方向键、F1-F12）
- [ ] 文本输入（英文）
- [ ] 文本输入（中文）
- [ ] 特殊字符

### 网络异常

- [ ] Wi-Fi 断开自动重连
- [ ] 主机离线后重连
- [ ] 重复加入房间报错

### 边界场景

- [ ] 多房间隔离
- [ ] 主机重启后恢复连接
- [ ] 多设备同时在线

---

## 相关文档

- [信令协议](./SIGNALING_PROTOCOL.md) — 信令消息格式
- [Windows 主机 README](../host/windows/README.md) — 主机运行参数说明
- [E2E 测试脚本](../e2e/scripts/run-packaging-gate.ps1) — 自动化门禁

---

> 最后更新：2026-04-15
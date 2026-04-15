# 信令协议 v1

实现见 [server/signaling/src/index.js](../server/signaling/src/index.js)（消息为 JSON，无单独 TypeScript 定义）。

## 传输

- WebSocket，文本帧 JSON。
- 首条消息必须包含 `type` 与 join 所需字段。

## 客户端 → 服务器

### `join`

```json
{
  "type": "join",
  "roomId": "string",
  "role": "host" | "client",
  "displayName": "string (optional)"
}
```

- 同一 `roomId` 内至多一名 `host`、一名 `client`。
- 第二个同角色加入会收到错误并断开（实现可改为排队，当前为拒绝）。

### `signal`

```json
{
  "type": "signal",
  "payload": {
    "kind": "offer" | "answer" | "ice",
    "sdp": "string (offer/answer)",
    "candidate": "string (ice)",
    "sdpMid": "string|null (ice)",
    "sdpMLineIndex": "number|null (ice)"
  }
}
```

### `ping`

```json
{ "type": "ping", "id": "optional string" }
```

## 服务器 → 客户端

### `joined`

```json
{
  "type": "joined",
  "roomId": "string",
  "role": "host" | "client",
  "peerPresent": true
}
```

### `peer`

对等端上线/下线。

```json
{ "type": "peer", "event": "joined" | "left", "role": "host" | "client" }
```

### `signal`

透传对端的 WebRTC 信令，`payload` 同上传。

### `pong`

```json
{ "type": "pong", "id": "string|null" }
```

### `error`

```json
{ "type": "error", "code": "string", "message": "string" }
```

## 控制消息（DataChannel，主机侧解析）

JSON 文本，与 [host/windows 控制契约](../host/windows/README.md) 一致（`ctrl:v1`）。

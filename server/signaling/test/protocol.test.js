import test from "node:test";
import assert from "node:assert/strict";
import { WebSocket } from "ws";
import { createSignalingServer } from "../src/index.js";

const WS_CONNECT_TIMEOUT = 5000;
const MSG_TIMEOUT = 3000;

function wait(ms) {
  return new Promise((r) => setTimeout(r, ms));
}

function wsConnect(url) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(url);
    const timer = setTimeout(() => {
      ws.close();
      reject(new Error("connect timeout"));
    }, WS_CONNECT_TIMEOUT);
    ws.on("open", () => {
      clearTimeout(timer);
      resolve(ws);
    });
    ws.on("error", (err) => {
      clearTimeout(timer);
      reject(err);
    });
  });
}

function nextMessage(ws) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      reject(new Error("message timeout"));
    }, MSG_TIMEOUT);
    ws.once("message", (data) => {
      clearTimeout(timer);
      resolve(JSON.parse(data.toString()));
    });
    ws.once("close", () => {
      clearTimeout(timer);
      resolve(null);
    });
    ws.once("error", () => {
      clearTimeout(timer);
      resolve(null);
    });
  });
}

function collectMessages(ws, count, timeoutMs = MSG_TIMEOUT) {
  return new Promise((resolve, reject) => {
    const msgs = [];
    const timer = setTimeout(() => {
      resolve(msgs);
    }, timeoutMs);
    ws.on("message", (data) => {
      msgs.push(JSON.parse(data.toString()));
      if (msgs.length >= count) {
        clearTimeout(timer);
        resolve(msgs);
      }
    });
    ws.on("error", () => {
      clearTimeout(timer);
      resolve(msgs);
    });
  });
}

function wsClose(ws) {
  return new Promise((resolve) => {
    if (ws.readyState === ws.CLOSED) return resolve();
    ws.on("close", resolve);
    ws.close();
  });
}

let server;
let baseUrl;
let httpServer;

test.before(async () => {
  server = createSignalingServer();
  httpServer = server.httpServer;
  await new Promise((r) => httpServer.listen(0, r));
  const addr = httpServer.address();
  baseUrl = `ws://127.0.0.1:${addr.port}`;
});

test.after(async () => {
  server.wss.close();
  await new Promise((r) => httpServer.close(r));
});

// ── HTTP endpoint ──

test("HTTP GET / returns signaling version text", async () => {
  const addr = httpServer.address();
  const res = await fetch(`http://127.0.0.1:${addr.port}/`);
  assert.equal(res.status, 200);
  const text = await res.text();
  assert.ok(text.includes("iphone-remote signaling"));
});

// ── Join message ──

test("join as host succeeds", async () => {
  const ws = await wsConnect(baseUrl);
  ws.send(JSON.stringify({ type: "join", roomId: "room1", role: "host" }));
  const msg = await nextMessage(ws);
  assert.equal(msg.type, "joined");
  assert.equal(msg.roomId, "room1");
  assert.equal(msg.role, "host");
  assert.equal(msg.peerPresent, false);
  await wsClose(ws);
});

test("join as client succeeds", async () => {
  const ws = await wsConnect(baseUrl);
  ws.send(JSON.stringify({ type: "join", roomId: "room2", role: "client" }));
  const msg = await nextMessage(ws);
  assert.equal(msg.type, "joined");
  assert.equal(msg.role, "client");
  assert.equal(msg.peerPresent, false);
  await wsClose(ws);
});

test("join with missing roomId returns error and closes", async () => {
  const ws = await wsConnect(baseUrl);
  ws.send(JSON.stringify({ type: "join", roomId: "", role: "host" }));
  const msg = await nextMessage(ws);
  assert.equal(msg.type, "error");
  assert.equal(msg.code, "invalid_join");
  await wait(200);
  assert.equal(ws.readyState, ws.CLOSED);
});

test("join with invalid role returns error and closes", async () => {
  const ws = await wsConnect(baseUrl);
  ws.send(JSON.stringify({ type: "join", roomId: "r", role: "observer" }));
  const msg = await nextMessage(ws);
  assert.equal(msg.type, "error");
  assert.equal(msg.code, "invalid_join");
  await wait(200);
  assert.equal(ws.readyState, ws.CLOSED);
});

test("join with missing role returns error", async () => {
  const ws = await wsConnect(baseUrl);
  ws.send(JSON.stringify({ type: "join", roomId: "r" }));
  const msg = await nextMessage(ws);
  assert.equal(msg.type, "error");
  assert.equal(msg.code, "invalid_join");
  await wsClose(ws);
});

test("duplicate host join returns host_taken error and closes", async () => {
  const ws1 = await wsConnect(baseUrl);
  ws1.send(JSON.stringify({ type: "join", roomId: "duphost", role: "host" }));
  await nextMessage(ws1);

  const ws2 = await wsConnect(baseUrl);
  ws2.send(JSON.stringify({ type: "join", roomId: "duphost", role: "host" }));
  const msg = await nextMessage(ws2);
  assert.equal(msg.type, "error");
  assert.equal(msg.code, "host_taken");
  await wait(200);
  assert.equal(ws2.readyState, ws2.CLOSED);
  await wsClose(ws1);
});

test("duplicate client join returns client_taken error and closes", async () => {
  const ws1 = await wsConnect(baseUrl);
  ws1.send(JSON.stringify({ type: "join", roomId: "dupclient", role: "client" }));
  await nextMessage(ws1);

  const ws2 = await wsConnect(baseUrl);
  ws2.send(JSON.stringify({ type: "join", roomId: "dupclient", role: "client" }));
  const msg = await nextMessage(ws2);
  assert.equal(msg.type, "error");
  assert.equal(msg.code, "client_taken");
  await wsClose(ws1);
});

test("join trims roomId whitespace", async () => {
  const ws = await wsConnect(baseUrl);
  ws.send(JSON.stringify({ type: "join", roomId: "  spacedroom  ", role: "host" }));
  const msg = await nextMessage(ws);
  assert.equal(msg.roomId, "spacedroom");
  await wsClose(ws);
});

test("join notifies peer when both host and client are present", async () => {
  const hostWs = await wsConnect(baseUrl);
  hostWs.send(JSON.stringify({ type: "join", roomId: "peertest", role: "host" }));
  await nextMessage(hostWs);

  const clientWs = await wsConnect(baseUrl);
  const peerMsgs = collectMessages(hostWs, 1);

  clientWs.send(JSON.stringify({ type: "join", roomId: "peertest", role: "client" }));
  const clientMsg = await nextMessage(clientWs);
  assert.equal(clientMsg.type, "joined");
  assert.equal(clientMsg.peerPresent, true);

  const hostPeer = (await peerMsgs)[0];
  assert.equal(hostPeer.type, "peer");
  assert.equal(hostPeer.event, "joined");
  assert.equal(hostPeer.role, "client");

  await wsClose(hostWs);
  await wsClose(clientWs);
});

test("host joins with client already present sees peerPresent=true", async () => {
  const clientWs = await wsConnect(baseUrl);
  clientWs.send(JSON.stringify({ type: "join", roomId: "reversejoin", role: "client" }));
  await nextMessage(clientWs);

  const hostWs = await wsConnect(baseUrl);
  hostWs.send(JSON.stringify({ type: "join", roomId: "reversejoin", role: "host" }));
  const msg = await nextMessage(hostWs);
  assert.equal(msg.type, "joined");
  assert.equal(msg.peerPresent, true);

  await wsClose(hostWs);
  await wsClose(clientWs);
});

// ── Ping/Pong ──

test("ping returns pong with matching id", async () => {
  const ws = await wsConnect(baseUrl);
  ws.send(JSON.stringify({ type: "ping", id: "abc123" }));
  const msg = await nextMessage(ws);
  assert.equal(msg.type, "pong");
  assert.equal(msg.id, "abc123");
  await wsClose(ws);
});

test("ping without id returns pong with null id", async () => {
  const ws = await wsConnect(baseUrl);
  ws.send(JSON.stringify({ type: "ping" }));
  const msg = await nextMessage(ws);
  assert.equal(msg.type, "pong");
  assert.equal(msg.id, null);
  await wsClose(ws);
});

// ── Invalid messages ──

test("invalid JSON returns bad_json error", async () => {
  const ws = await wsConnect(baseUrl);
  ws.send("not json at all");
  const msg = await nextMessage(ws);
  assert.equal(msg.type, "error");
  assert.equal(msg.code, "bad_json");
  await wsClose(ws);
});

test("message before join returns not_joined error", async () => {
  const ws = await wsConnect(baseUrl);
  ws.send(JSON.stringify({ type: "signal", payload: {} }));
  const msg = await nextMessage(ws);
  assert.equal(msg.type, "error");
  assert.equal(msg.code, "not_joined");
  await wsClose(ws);
});

test("unknown message type after join returns unknown_type error", async () => {
  const ws = await wsConnect(baseUrl);
  ws.send(JSON.stringify({ type: "join", roomId: "unknown1", role: "host" }));
  await nextMessage(ws);
  ws.send(JSON.stringify({ type: "foobar" }));
  const msg = await nextMessage(ws);
  assert.equal(msg.type, "error");
  assert.equal(msg.code, "unknown_type");
  assert.equal(msg.message, "foobar");
  await wsClose(ws);
});

// ── Signal forwarding ──

test("host signal forwarded to client", async () => {
  const hostWs = await wsConnect(baseUrl);
  hostWs.send(JSON.stringify({ type: "join", roomId: "sig1", role: "host" }));
  await nextMessage(hostWs);

  const clientWs = await wsConnect(baseUrl);
  clientWs.send(JSON.stringify({ type: "join", roomId: "sig1", role: "client" }));
  await nextMessage(clientWs);
  await nextMessage(hostWs);

  const payload = { kind: "offer", sdp: "fake-sdp" };
  hostWs.send(JSON.stringify({ type: "signal", payload }));

  const clientMsg = await nextMessage(clientWs);
  assert.equal(clientMsg.type, "signal");
  assert.deepEqual(clientMsg.payload, payload);
  await wsClose(hostWs);
  await wsClose(clientWs);
});

test("client signal forwarded to host", async () => {
  const hostWs = await wsConnect(baseUrl);
  hostWs.send(JSON.stringify({ type: "join", roomId: "sig2", role: "host" }));
  await nextMessage(hostWs);

  const clientWs = await wsConnect(baseUrl);
  clientWs.send(JSON.stringify({ type: "join", roomId: "sig2", role: "client" }));
  await nextMessage(clientWs);
  await nextMessage(hostWs);

  const payload = { kind: "answer", sdp: "fake-answer-sdp" };
  clientWs.send(JSON.stringify({ type: "signal", payload }));

  const hostMsg = await nextMessage(hostWs);
  assert.equal(hostMsg.type, "signal");
  assert.deepEqual(hostMsg.payload, payload);
  await wsClose(hostWs);
  await wsClose(clientWs);
});

test("signal not forwarded when peer absent", async () => {
  const ws = await wsConnect(baseUrl);
  ws.send(JSON.stringify({ type: "join", roomId: "nop4", role: "host" }));
  await nextMessage(ws);
  ws.send(JSON.stringify({ type: "signal", payload: { kind: "ice" } }));
  const msgs = await collectMessages(ws, 1, 500);
  assert.equal(msgs.length, 0);
  await wsClose(ws);
});

// ── Disconnect / peer left ──

test("host disconnect notifies client with peer left", async () => {
  const hostWs = await wsConnect(baseUrl);
  hostWs.send(JSON.stringify({ type: "join", roomId: "disc1", role: "host" }));
  await nextMessage(hostWs);

  const clientWs = await wsConnect(baseUrl);
  clientWs.send(JSON.stringify({ type: "join", roomId: "disc1", role: "client" }));
  await nextMessage(clientWs);
  await nextMessage(hostWs);

  const peerLeft = collectMessages(clientWs, 1);
  await wsClose(hostWs);
  const msg = (await peerLeft)[0];
  assert.equal(msg.type, "peer");
  assert.equal(msg.event, "left");
  assert.equal(msg.role, "host");
  await wsClose(clientWs);
});

test("client disconnect notifies host with peer left", async () => {
  const hostWs = await wsConnect(baseUrl);
  hostWs.send(JSON.stringify({ type: "join", roomId: "disc2", role: "host" }));
  await nextMessage(hostWs);

  const clientWs = await wsConnect(baseUrl);
  clientWs.send(JSON.stringify({ type: "join", roomId: "disc2", role: "client" }));
  await nextMessage(clientWs);
  await nextMessage(hostWs);

  const peerLeft = collectMessages(hostWs, 1);
  await wsClose(clientWs);
  const msg = (await peerLeft)[0];
  assert.equal(msg.type, "peer");
  assert.equal(msg.event, "left");
  assert.equal(msg.role, "client");
  await wsClose(hostWs);
});

test("room cleaned up after both disconnect", async () => {
  const hostWs = await wsConnect(baseUrl);
  hostWs.send(JSON.stringify({ type: "join", roomId: "cleanup1", role: "host" }));
  await nextMessage(hostWs);

  const clientWs = await wsConnect(baseUrl);
  clientWs.send(JSON.stringify({ type: "join", roomId: "cleanup1", role: "client" }));
  await nextMessage(clientWs);
  await nextMessage(hostWs);

  await wsClose(hostWs);
  await wsClose(clientWs);
  await wait(200);

  assert.equal(server.rooms.size === 0 || !server.rooms.has("cleanup1"), true);
});

// ── ICE signal forwarding ──

test("ICE candidate signal forwarded from host to client", async () => {
  const hostWs = await wsConnect(baseUrl);
  hostWs.send(JSON.stringify({ type: "join", roomId: "ice1", role: "host" }));
  await nextMessage(hostWs);

  const clientWs = await wsConnect(baseUrl);
  clientWs.send(JSON.stringify({ type: "join", roomId: "ice1", role: "client" }));
  await nextMessage(clientWs);
  await nextMessage(hostWs);

  const icePayload = { kind: "ice", candidate: "a=candidate:123", sdpMid: "0", sdpMLineIndex: 0 };
  hostWs.send(JSON.stringify({ type: "signal", payload: icePayload }));

  const msg = await nextMessage(clientWs);
  assert.equal(msg.type, "signal");
  assert.equal(msg.payload.kind, "ice");
  assert.equal(msg.payload.candidate, "a=candidate:123");
  await wsClose(hostWs);
  await wsClose(clientWs);
});

// ── Multiple rooms isolation ──

test("rooms are isolated from each other", async () => {
  const host1 = await wsConnect(baseUrl);
  host1.send(JSON.stringify({ type: "join", roomId: "roomA", role: "host" }));
  await nextMessage(host1);

  const host2 = await wsConnect(baseUrl);
  host2.send(JSON.stringify({ type: "join", roomId: "roomB", role: "host" }));
  await nextMessage(host2);

  const client1 = await wsConnect(baseUrl);
  client1.send(JSON.stringify({ type: "join", roomId: "roomA", role: "client" }));
  await nextMessage(client1);

  const client2 = await wsConnect(baseUrl);
  client2.send(JSON.stringify({ type: "join", roomId: "roomB", role: "client" }));
  await nextMessage(client2);

  const payload = { kind: "offer", sdp: "roomA-offer" };
  host1.send(JSON.stringify({ type: "signal", payload }));

  const msg = await nextMessage(client1);
  assert.equal(msg.payload.sdp, "roomA-offer");

  const noMsg = await collectMessages(client2, 1, 500);
  assert.equal(noMsg.length, 0);

  await wsClose(host1);
  await wsClose(host2);
  await wsClose(client1);
  await wsClose(client2);
});

// ── Join after peer left allows rejoin ──

test("new host can join after previous host left", async () => {
  const host1 = await wsConnect(baseUrl);
  host1.send(JSON.stringify({ type: "join", roomId: "rejoin1", role: "host" }));
  await nextMessage(host1);
  await wsClose(host1);
  await wait(200);

  const host2 = await wsConnect(baseUrl);
  host2.send(JSON.stringify({ type: "join", roomId: "rejoin1", role: "host" }));
  const msg = await nextMessage(host2);
  assert.equal(msg.type, "joined");
  assert.equal(msg.role, "host");
  await wsClose(host2);
});

// ── Multiple joins from same connection not allowed (second join is unknown) ──

test("second join from same connection returns host_taken (same conn already hosts)", async () => {
  const ws = await wsConnect(baseUrl);
  ws.send(JSON.stringify({ type: "join", roomId: "doublejoin", role: "host" }));
  await nextMessage(ws);
  ws.send(JSON.stringify({ type: "join", roomId: "doublejoin", role: "host" }));
  const msg = await nextMessage(ws);
  assert.equal(msg.type, "error");
  assert.equal(msg.code, "host_taken");
  await wsClose(ws);
});

// ── displayName field is accepted ──

test("join with displayName is accepted", async () => {
  const ws = await wsConnect(baseUrl);
  ws.send(JSON.stringify({ type: "join", roomId: "name1", role: "host", displayName: "MyPC" }));
  const msg = await nextMessage(ws);
  assert.equal(msg.type, "joined");
  await wsClose(ws);
});

// ── Ping works before join ──

test("ping works before join", async () => {
  const ws = await wsConnect(baseUrl);
  ws.send(JSON.stringify({ type: "ping", id: "pre-join" }));
  const msg = await nextMessage(ws);
  assert.equal(msg.type, "pong");
  assert.equal(msg.id, "pre-join");
  await wsClose(ws);
});

// ── Rooms map is accessible ──

test("rooms map tracks active rooms", async () => {
  const host = await wsConnect(baseUrl);
  host.send(JSON.stringify({ type: "join", roomId: "tracking1", role: "host" }));
  await nextMessage(host);
  assert.ok(server.rooms.has("tracking1"));
  await wsClose(host);
  await wait(200);
  assert.ok(!server.rooms.has("tracking1"));
});

// ── WebSocket error triggers cleanup ──

test("ws error after join triggers removeConnection", async () => {
  const host = await wsConnect(baseUrl);
  host.send(JSON.stringify({ type: "join", roomId: "wserr1", role: "host" }));
  await nextMessage(host);
  assert.ok(server.rooms.has("wserr1"));
  host.terminate();
  await wait(200);
  assert.ok(!server.rooms.has("wserr1"));
});
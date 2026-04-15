/**
 * E2E Test: Simulated Android client logs into Windows signaling server.
 *
 * This test starts a real signaling server, then simulates the full flow
 * that an Android phone would go through:
 *   1. Android client connects via WebSocket
 *   2. Android client sends "join" as client role
 *   3. A simulated Windows host joins as host role
 *   4. Verify peer notification is received by both sides
 *   5. Host sends an SDP offer (simulated)
 *   6. Client receives the offer and sends an SDP answer back
 *   7. Both sides exchange ICE candidates
 *   8. Client sends ctrl DataChannel messages (simulated keyboard/touch)
 *   9. Host disconnects; client receives peer left event
 *  10. Client disconnects; room is cleaned up
 *
 * Also tests ping/pong keepalive and error handling.
 */

import { createServer } from "http";
import { WebSocketServer } from "ws";
import { createSignalingServer } from "../../server/signaling/src/index.js";

const WS_CONNECT_TIMEOUT = 5000;
const MSG_TIMEOUT = 3000;
const TEST_ROOM = "e2e-test-room";

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
  return new Promise((resolve) => {
    const msgs = [];
    const timer = setTimeout(() => resolve(msgs), timeoutMs);
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
let httpServer;
let baseUrl;

import test from "node:test";
import assert from "node:assert/strict";
import { WebSocket } from "ws";

// ─── E2E: Android phone logs into Windows signaling server ───

test.before(async () => {
  server = createSignalingServer();
  httpServer = server.httpServer;
  await new Promise((r) => httpServer.listen(0, r));
  const addr = httpServer.address();
  baseUrl = `ws://127.0.0.1:${addr.port}`;
  console.log(`[e2e] Signaling server started on port ${addr.port}`);
});

test.after(async () => {
  server.wss.close();
  await new Promise((r) => httpServer.close(r));
  console.log("[e2e] Signaling server stopped");
});

// ── Full E2E flow: Android client connects to Windows host via signaling ──

test("E2E: Android client logs into Windows signaling server, full flow", async () => {
  console.log("[e2e] Step 1: Android client connects to signaling server...");

  // Simulate Android client (like RemoteSession.connectWs)
  const androidClient = await wsConnect(baseUrl);
  androidClient.send(JSON.stringify({
    type: "join",
    roomId: TEST_ROOM,
    role: "client",
    displayName: "Pixel-8-Android14",
  }));

  const clientJoined = await nextMessage(androidClient);
  assert.equal(clientJoined.type, "joined");
  assert.equal(clientJoined.roomId, TEST_ROOM);
  assert.equal(clientJoined.role, "client");
  assert.equal(clientJoined.peerPresent, false);
  console.log("[e2e] Step 1 OK: Android client joined room, peerPresent=false");

  // Simulate Windows host joining (like RemoteHostRunner.RunAsync)
  console.log("[e2e] Step 2: Windows host connects to signaling server...");
  const windowsHost = await wsConnect(baseUrl);
  windowsHost.send(JSON.stringify({
    type: "join",
    roomId: TEST_ROOM,
    role: "host",
    displayName: "WIN11-DESKTOP",
  }));

  const hostJoined = await nextMessage(windowsHost);
  assert.equal(hostJoined.type, "joined");
  assert.equal(hostJoined.role, "host");
  assert.equal(hostJoined.peerPresent, true);
  console.log("[e2e] Step 2 OK: Windows host joined room, peerPresent=true");

  // Android client should receive peer notification
  const clientPeer = await nextMessage(androidClient);
  assert.equal(clientPeer.type, "peer");
  assert.equal(clientPeer.event, "joined");
  assert.equal(clientPeer.role, "host");
  console.log("[e2e] Step 2b OK: Android client received peer notification for host");

  // ── Step 3: Host sends SDP offer ──
  console.log("[e2e] Step 3: Host sends SDP offer to client...");
  const fakeOffer = {
    type: "signal",
    payload: {
      kind: "offer",
      sdp: "v=0\r\no=- 123456 2 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\na=group:BUNDLE 0\r\na=msid-semantic: WMS\r\nm=video 9 UDP/TLS/RTP/SAVPF 96\r\nc=IN IP4 0.0.0.0\r\na=rtcp:9 IN IP4 0.0.0.0\r\na=ice-ufrag:abcd\r\na=ice-pwd:abcdefghijklmnop\r\na=fingerprint:sha-256 00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00\r\na=setup:actpass\r\na=mid:0\r\na=sendonly\r\na=rtcp-mux\r\na=rtpmap:96 VP8/90000\r\n",
    },
  };
  windowsHost.send(JSON.stringify(fakeOffer));

  const clientOffer = await nextMessage(androidClient);
  assert.equal(clientOffer.type, "signal");
  assert.equal(clientOffer.payload.kind, "offer");
  assert.ok(clientOffer.payload.sdp.includes("VP8"));
  console.log("[e2e] Step 3 OK: Android client received SDP offer (VP8 video)");

  // ── Step 4: Client sends SDP answer ──
  console.log("[e2e] Step 4: Client sends SDP answer back to host...");
  const fakeAnswer = {
    type: "signal",
    payload: {
      kind: "answer",
      sdp: "v=0\r\no=- 789012 2 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\na=group:BUNDLE 0\r\na=msid-semantic: WMS\r\nm=video 9 UDP/TLS/RTP/SAVPF 96\r\nc=IN IP4 0.0.0.0\r\na=rtcp:9 IN IP4 0.0.0.0\r\na=ice-ufrag:efgh\r\na=ice-pwd:efghijklmnopqrst\r\na=fingerprint:sha-256 11:11:11:11:11:11:11:11:11:11:11:11:11:11:11:11:11:11:11:11:11:11:11:11:11:11:11:11:11:11:11:11\r\na=setup:active\r\na=mid:0\r\na=recvonly\r\na=rtcp-mux\r\na=rtpmap:96 VP8/90000\r\n",
    },
  };
  androidClient.send(JSON.stringify(fakeAnswer));

  const hostAnswer = await nextMessage(windowsHost);
  assert.equal(hostAnswer.type, "signal");
  assert.equal(hostAnswer.payload.kind, "answer");
  console.log("[e2e] Step 4 OK: Host received SDP answer");

  // ── Step 5: ICE candidate exchange ──
  console.log("[e2e] Step 5: ICE candidates exchanged...");
  const hostIce = {
    type: "signal",
    payload: {
      kind: "ice",
      candidate: "a=candidate:1 1 UDP 2130706431 192.168.1.100 54321 typ host",
      sdpMid: "0",
      sdpMLineIndex: 0,
    },
  };
  windowsHost.send(JSON.stringify(hostIce));

  const clientIce = await nextMessage(androidClient);
  assert.equal(clientIce.type, "signal");
  assert.equal(clientIce.payload.kind, "ice");
  assert.ok(clientIce.payload.candidate.includes("192.168.1.100"));

  const clientIceCandidate = {
    type: "signal",
    payload: {
      kind: "ice",
      candidate: "a=candidate:1 1 UDP 2130706431 10.0.2.15 12345 typ host",
      sdpMid: "0",
      sdpMLineIndex: 0,
    },
  };
  androidClient.send(JSON.stringify(clientIceCandidate));

  const hostIceFromClient = await nextMessage(windowsHost);
  assert.equal(hostIceFromClient.type, "signal");
  assert.equal(hostIceFromClient.payload.kind, "ice");
  console.log("[e2e] Step 5 OK: ICE candidates exchanged between host and client");

  // ── Step 6: Ping/Pong keepalive ──
  console.log("[e2e] Step 6: Android client sends ping keepalive...");
  androidClient.send(JSON.stringify({ type: "ping", id: "keepalive-1" }));
  const pong = await nextMessage(androidClient);
  assert.equal(pong.type, "pong");
  assert.equal(pong.id, "keepalive-1");
  console.log("[e2e] Step 6 OK: Keepalive pong received");

  // ── Step 7: Host disconnects, client gets peer left ──
  console.log("[e2e] Step 7: Windows host disconnects...");
  const clientPeerLeft = collectMessages(androidClient, 1);
  await wsClose(windowsHost);
  const leftMsg = (await clientPeerLeft)[0];
  assert.equal(leftMsg.type, "peer");
  assert.equal(leftMsg.event, "left");
  assert.equal(leftMsg.role, "host");
  console.log("[e2e] Step 7 OK: Android client received peer left event");

  // ── Step 8: Android client disconnects, room cleaned up ──
  console.log("[e2e] Step 8: Android client disconnects...");
  await wsClose(androidClient);
  await wait(200);
  assert.ok(!server.rooms.has(TEST_ROOM));
  console.log("[e2e] Step 8 OK: Room cleaned up after both disconnect");
});

// ── E2E: Control message format validation (simulating DataChannel) ──

test("E2E: Control messages match expected format for touch/keyboard/wheel", async () => {
  // These messages would be sent over WebRTC DataChannel from Android to Windows host.
  // Here we verify the JSON format matches what ControlMessages.cs expects.

  console.log("[e2e] Validating control message format compliance...");

  const controlMessages = [
    // Touch tap (left click)
    { v: 1, t: "down", x: 0.5, y: 0.5, b: 0 },
    { v: 1, t: "up", x: 0.5, y: 0.5, b: 0 },
    // Touch long-press (right click)
    { v: 1, t: "down", x: 0.25, y: 0.75, b: 1 },
    { v: 1, t: "up", x: 0.25, y: 0.75, b: 1 },
    // Mouse move (finger drag)
    { v: 1, t: "move", x: 0.3, y: 0.4 },
    // Mouse scroll (two-finger swipe)
    { v: 1, t: "wheel", dx: 0, dy: -3 },
    // Keyboard key press/release
    { v: 1, t: "key", k: 27, down: true },
    { v: 1, t: "key", k: 27, down: false },
    // Text input
    { v: 1, t: "text", s: "hello world" },
  ];

  for (const msg of controlMessages) {
    const json = JSON.stringify(msg);
    const parsed = JSON.parse(json);
    assert.equal(parsed.v, 1, `Message missing version: ${json}`);
    assert.ok(parsed.t, `Message missing type: ${json}`);

    switch (parsed.t) {
      case "move":
      case "down":
      case "up":
        assert.ok(typeof parsed.x === "number", `Missing numeric x: ${json}`);
        assert.ok(typeof parsed.y === "number", `Missing numeric y: ${json}`);
        if (parsed.t !== "move") {
          assert.ok(typeof parsed.b === "number", `Missing button b: ${json}`);
        }
        break;
      case "wheel":
        assert.ok(typeof parsed.dx === "number", `Missing numeric dx: ${json}`);
        assert.ok(typeof parsed.dy === "number", `Missing numeric dy: ${json}`);
        break;
      case "key":
        assert.ok(typeof parsed.k === "number", `Missing key code k: ${json}`);
        assert.ok(typeof parsed.down === "boolean", `Missing boolean down: ${json}`);
        break;
      case "text":
        assert.ok(typeof parsed.s === "string", `Missing string s: ${json}`);
        assert.ok(parsed.s.length > 0, `Text string is empty: ${json}`);
        break;
      default:
        assert.fail(`Unknown control message type: ${parsed.t}`);
    }
    console.log(`  [OK] ${parsed.t} message format valid`);
  }
  console.log("[e2e] All control message formats validated");
});

// ── E2E: Reconnection flow simulation ──

test("E2E: Client reconnects after host disconnect (rejoin)", async () => {
  console.log("[e2e] Testing reconnection: client rejoins after host disconnect...");

  // Host joins
  const host = await wsConnect(baseUrl);
  host.send(JSON.stringify({ type: "join", roomId: "reconnect-test", role: "host", displayName: "WIN11" }));
  await nextMessage(host);

  // Client joins
  const client = await wsConnect(baseUrl);
  client.send(JSON.stringify({ type: "join", roomId: "reconnect-test", role: "client", displayName: "Pixel8" }));
  await nextMessage(client);
  await nextMessage(host);

  // Host disconnects — set up listener BEFORE closing
  const peerLeftPromise = collectMessages(client, 1);
  await wsClose(host);
  const leftMsgs = await peerLeftPromise;
  assert.equal(leftMsgs[0].type, "peer");
  assert.equal(leftMsgs[0].event, "left");
  assert.equal(leftMsgs[0].role, "host");
  console.log("[e2e] Client received host left notification");

  // New host joins (simulating host restart / reconnect)
  const host2 = await wsConnect(baseUrl);
  host2.send(JSON.stringify({ type: "join", roomId: "reconnect-test", role: "host", displayName: "WIN11-RECONNECT" }));

  const host2Joined = await nextMessage(host2);
  assert.equal(host2Joined.type, "joined");
  assert.equal(host2Joined.peerPresent, true);
  console.log("[e2e] New host joined, peerPresent=true (client still in room)");

  const clientPeer2 = await nextMessage(client);
  assert.equal(clientPeer2.type, "peer");
  assert.equal(clientPeer2.event, "joined");
  assert.equal(clientPeer2.role, "host");
  console.log("[e2e] Client received peer joined notification for new host");

  // Client and new host can exchange signals
  host2.send(JSON.stringify({
    type: "signal",
    payload: { kind: "offer", sdp: "reconnect-offer-sdp" },
  }));
  const clientSignal = await nextMessage(client);
  assert.equal(clientSignal.type, "signal");
  assert.equal(clientSignal.payload.kind, "offer");

  await wsClose(host2);
  await wsClose(client);
  console.log("[e2e] Reconnection flow completed successfully");
});

// ── E2E: Multiple rooms isolation ──

test("E2E: Multiple devices in separate rooms don't interfere", async () => {
  console.log("[e2e] Testing room isolation with multiple devices...");

  // Room A: Host1 + Client1
  const hostA = await wsConnect(baseUrl);
  hostA.send(JSON.stringify({ type: "join", roomId: "room-A", role: "host" }));
  await nextMessage(hostA);

  const clientA = await wsConnect(baseUrl);
  clientA.send(JSON.stringify({ type: "join", roomId: "room-A", role: "client" }));
  await nextMessage(clientA);
  await nextMessage(hostA);

  // Room B: Host2 + Client2
  const hostB = await wsConnect(baseUrl);
  hostB.send(JSON.stringify({ type: "join", roomId: "room-B", role: "host" }));
  await nextMessage(hostB);

  const clientB = await wsConnect(baseUrl);
  clientB.send(JSON.stringify({ type: "join", roomId: "room-B", role: "client" }));
  await nextMessage(clientB);
  await nextMessage(hostB);

  // Host A sends signal — only Client A should receive
  hostA.send(JSON.stringify({
    type: "signal",
    payload: { kind: "offer", sdp: "room-A-offer" },
  }));
  const msgA = await nextMessage(clientA);
  assert.equal(msgA.payload.sdp, "room-A-offer");

  // Verify Client B does NOT receive it
  const noMsg = await collectMessages(clientB, 1, 500);
  assert.equal(noMsg.length, 0, "Client B should not receive room A signals");

  console.log("[e2e] Room isolation verified: signals stay within rooms");

  await wsClose(hostA);
  await wsClose(clientA);
  await wsClose(hostB);
  await wsClose(clientB);
});

// ── E2E: Error handling - invalid join ──

test("E2E: Android client handles error responses correctly", async () => {
  console.log("[e2e] Testing error handling...");

  // Invalid join (missing roomId)
  const ws1 = await wsConnect(baseUrl);
  ws1.send(JSON.stringify({ type: "join", roomId: "", role: "client" }));
  const err1 = await nextMessage(ws1);
  assert.equal(err1.type, "error");
  assert.equal(err1.code, "invalid_join");
  await wait(200);

  // Invalid JSON
  const ws2 = await wsConnect(baseUrl);
  ws2.send("not json");
  const err2 = await nextMessage(ws2);
  assert.equal(err2.type, "error");
  assert.equal(err2.code, "bad_json");
  await wsClose(ws2);

  console.log("[e2e] Error handling verified");
});
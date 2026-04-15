import { createServer } from "http";
import { WebSocketServer } from "ws";

const PORT = Number(process.env.PORT || 8787);

/** @typedef {{ ws: import('ws').WebSocket, roomId: string, role: 'host'|'client', id: string }} Connection */

export function createSignalingServer() {
  /** @type {Map<string, { host?: Connection, client?: Connection }>} */
  const rooms = new Map();

  function roomKey(roomId) {
    return roomId.trim();
  }

  function send(ws, obj) {
    if (ws.readyState === ws.OPEN) ws.send(JSON.stringify(obj));
  }

  function broadcastPeerEvent(roomId, except, event, role) {
    const r = rooms.get(roomId);
    if (!r) return;
    const msg = { type: "peer", event, role };
    if (r.host && r.host.ws !== except) send(r.host.ws, msg);
    if (r.client && r.client.ws !== except) send(r.client.ws, msg);
  }

  function forwardSignal(from, roomId, payload) {
    const r = rooms.get(roomId);
    if (!r) return;
    const target = from.role === "host" ? r.client : r.host;
    if (target) {
      send(target.ws, { type: "signal", payload });
    }
  }

  function removeConnection(conn) {
    const r = rooms.get(conn.roomId);
    if (!r) return;
    if (r.host?.id === conn.id) {
      delete r.host;
      broadcastPeerEvent(conn.roomId, null, "left", "host");
    }
    if (r.client?.id === conn.id) {
      delete r.client;
      broadcastPeerEvent(conn.roomId, null, "left", "client");
    }
    if (!r.host && !r.client) rooms.delete(conn.roomId);
  }

  const httpServer = createServer((_req, res) => {
    res.writeHead(200, { "Content-Type": "text/plain" });
    res.end("iphone-remote signaling v1\n");
  });

  const wss = new WebSocketServer({ server: httpServer });

  wss.on("connection", (ws) => {
    /** @type {Connection | null} */
    let conn = null;

    ws.on("message", (data) => {
      let msg;
      try {
        msg = JSON.parse(data.toString());
      } catch {
        send(ws, { type: "error", code: "bad_json", message: "Invalid JSON" });
        return;
      }

      if (msg.type === "ping") {
        send(ws, { type: "pong", id: msg.id ?? null });
        return;
      }

      if (msg.type === "join") {
        const roomId = roomKey(msg.roomId || "");
        const role = msg.role;
        if (!roomId || (role !== "host" && role !== "client")) {
          send(ws, {
            type: "error",
            code: "invalid_join",
            message: "roomId and role (host|client) required",
          });
          ws.close();
          return;
        }
        let r = rooms.get(roomId);
        if (!r) {
          r = {};
          rooms.set(roomId, r);
        }
        if (role === "host" && r.host) {
          send(ws, {
            type: "error",
            code: "host_taken",
            message: "Room already has a host",
          });
          ws.close();
          return;
        }
        if (role === "client" && r.client) {
          send(ws, {
            type: "error",
            code: "client_taken",
            message: "Room already has a client",
          });
          ws.close();
          return;
        }

        const id = `${role}-${Math.random().toString(36).slice(2, 10)}`;
        conn = { ws, roomId, role, id };
        if (role === "host") r.host = conn;
        else r.client = conn;

        const peerPresent = Boolean(role === "host" ? r.client : r.host);
        send(ws, {
          type: "joined",
          roomId,
          role,
          peerPresent,
        });
        broadcastPeerEvent(roomId, ws, "joined", role);
        return;
      }

      if (!conn) {
        send(ws, { type: "error", code: "not_joined", message: "Send join first" });
        return;
      }

      if (msg.type === "signal") {
        forwardSignal(conn, conn.roomId, msg.payload);
        return;
      }

      send(ws, { type: "error", code: "unknown_type", message: msg.type });
    });

    ws.on("close", () => {
      if (conn) removeConnection(conn);
    });

    ws.on("error", () => {
      if (conn) removeConnection(conn);
    });
  });

  return { httpServer, wss, rooms };
}
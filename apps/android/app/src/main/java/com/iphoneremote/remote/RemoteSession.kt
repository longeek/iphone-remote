package com.iphoneremote.remote

import android.content.Context
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import org.json.JSONObject
import org.webrtc.DataChannel
import org.webrtc.DefaultVideoDecoderFactory
import org.webrtc.EglBase
import org.webrtc.IceCandidate
import org.webrtc.MediaConstraints
import org.webrtc.MediaStream
import org.webrtc.PeerConnection
import org.webrtc.PeerConnectionFactory
import org.webrtc.RtpReceiver
import org.webrtc.SessionDescription
import org.webrtc.SurfaceViewRenderer
import org.webrtc.VideoTrack
import java.nio.ByteBuffer
import java.nio.charset.StandardCharsets
import java.util.concurrent.ConcurrentLinkedQueue

class RemoteSession(
    private val context: Context,
    private val scope: CoroutineScope,
    private val onStatus: (String) -> Unit,
    private val onError: (String) -> Unit,
) {
    private val eglBase: EglBase = EglBase.create()
    private var factory: PeerConnectionFactory? = null
    private var pc: PeerConnection? = null
    private var ws: WebSocket? = null
    private var ctrl: DataChannel? = null
    private var remoteVideo: VideoTrack? = null
    private var videoSink: SurfaceViewRenderer? = null
    private val pendingRemoteIce = ConcurrentLinkedQueue<IceCandidate>()
    private var remoteDescSet = false
    private var job: Job? = null
    @Volatile
    private var sessionStarted = false

    private var currentSignalingUrl: String = ""
    private var currentRoomId: String = ""
    private var currentIceServers: List<String> = emptyList()
    private var reconnectJob: Job? = null
    @Volatile
    private var intentionallyDisconnected = false
    private val maxReconnectAttempts = 5
    private var reconnectAttempts = 0

    fun start(
        signalingWsUrl: String,
        roomId: String,
        renderer: SurfaceViewRenderer,
        iceServersFromEnv: List<String> = emptyList(),
    ) {
        if (sessionStarted) return
        sessionStarted = true
        intentionallyDisconnected = false
        reconnectAttempts = 0
        currentSignalingUrl = signalingWsUrl
        currentRoomId = roomId
        currentIceServers = iceServersFromEnv
        job?.cancel()
        job = scope.launch(Dispatchers.Main.immediate) {
            try {
                initFactory()
                val ice = buildIceServers(currentIceServers)
                val rtcConfig = PeerConnection.RTCConfiguration(ice).apply {
                    sdpSemantics = PeerConnection.SdpSemantics.UNIFIED_PLAN
                }
                pc = factory!!.createPeerConnection(
                    rtcConfig,
                    object : PeerConnection.Observer {
                        override fun onSignalingChange(p0: PeerConnection.SignalingState?) {}
                        override fun onIceConnectionChange(state: PeerConnection.IceConnectionState?) {
                            when (state) {
                                PeerConnection.IceConnectionState.FAILED -> {
                                    onStatus("ICE connection failed")
                                }
                                PeerConnection.IceConnectionState.DISCONNECTED -> {
                                    onStatus("ICE disconnected")
                                }
                                PeerConnection.IceConnectionState.CONNECTED -> {
                                    reconnectAttempts = 0
                                    onStatus("ICE connected")
                                }
                                else -> {}
                            }
                        }
                        override fun onIceConnectionReceivingChange(p0: Boolean) {}
                        override fun onIceGatheringChange(p0: PeerConnection.IceGatheringState?) {}
                        override fun onIceCandidate(candidate: IceCandidate?) {
                            candidate ?: return
                            sendSignal(
                                JSONObject().apply {
                                    put("kind", "ice")
                                    put("candidate", candidate.sdp)
                                    put("sdpMid", candidate.sdpMid)
                                    put("sdpMLineIndex", candidate.sdpMLineIndex)
                                },
                            )
                        }

                        override fun onIceCandidatesRemoved(p0: Array<out IceCandidate>?) {}
                        override fun onAddStream(p0: MediaStream?) {}
                        override fun onRemoveStream(p0: MediaStream?) {}
                        override fun onDataChannel(dc: DataChannel?) {
                            if (dc?.label() == "ctrl") {
                                ctrl = dc
                                dc.registerObserver(
                                    object : DataChannel.Observer {
                                        override fun onBufferedAmountChange(p0: Long) {}
                                        override fun onStateChange() {}
                                        override fun onMessage(buffer: DataChannel.Buffer?) {
                                            // host -> client messages (optional)
                                        }
                                    },
                                )
                                onStatus("Control channel ready")
                            }
                        }

                        override fun onRenegotiationNeeded() {}
                        override fun onAddTrack(
                            receiver: RtpReceiver?,
                            streams: Array<out MediaStream>?,
                        ) {
                            val track = receiver?.track() as? VideoTrack ?: return
                            remoteVideo = track
                            track.setEnabled(true)
                            videoSink = renderer
                            track.addSink(renderer)
                            onStatus("Video playing")
                        }
                    },
                ) ?: throw IllegalStateException("createPeerConnection failed")

                connectWs(currentSignalingUrl, currentRoomId)
            } catch (e: Exception) {
                onError(e.message ?: "start failed")
            }
        }
    }

    fun disconnect() {
        intentionallyDisconnected = true
        reconnectJob?.cancel()
        reconnectJob = null
        closeConnection()
        onStatus("Disconnected")
    }

    private fun closeConnection() {
        try {
            val v = remoteVideo
            val s = videoSink
            if (v != null && s != null) v.removeSink(s)
        } catch (_: Exception) {
        }
        remoteVideo = null
        videoSink = null
        ctrl = null
        try {
            pc?.close()
        } catch (_: Exception) {
        }
        pc = null
        remoteDescSet = false
        pendingRemoteIce.clear()
        try {
            ws?.close(1000, "disconnect")
        } catch (_: Exception) {
        }
        ws = null
    }

    fun dispose() {
        sessionStarted = false
        intentionallyDisconnected = true
        reconnectJob?.cancel()
        reconnectJob = null
        job?.cancel()
        closeConnection()
        try {
            factory?.dispose()
        } catch (_: Exception) {
        }
        factory = null
        try {
            eglBase.release()
        } catch (_: Exception) {
        }
    }

    fun sendControlJson(json: String) {
        val dc = ctrl ?: return
        val buf = ByteBuffer.wrap(json.toByteArray(StandardCharsets.UTF_8))
        dc.send(DataChannel.Buffer(buf, false))
    }

    fun eglContext(): EglBase.Context = eglBase.eglBaseContext

    private fun initFactory() {
        if (factory != null) return
        PeerConnectionFactory.initialize(
            PeerConnectionFactory.InitializationOptions.builder(context.applicationContext)
                .createInitializationOptions(),
        )
        val enc = org.webrtc.DefaultVideoEncoderFactory(
            eglBase.eglBaseContext,
            true,
            true,
        )
        val dec = DefaultVideoDecoderFactory(eglBase.eglBaseContext)
        factory = PeerConnectionFactory.builder()
            .setVideoEncoderFactory(enc)
            .setVideoDecoderFactory(dec)
            .createPeerConnectionFactory()
    }

    private fun buildIceServers(extra: List<String>): List<PeerConnection.IceServer> {
        val list = ArrayList<PeerConnection.IceServer>()
        for (e in extra) {
            val parts = e.split(";").map { it.trim() }.filter { it.isNotEmpty() }
            if (parts.isEmpty()) continue
            val b = PeerConnection.IceServer.builder(parts[0])
            if (parts.size >= 3) {
                b.setUsername(parts[1])
                b.setPassword(parts[2])
            }
            list.add(b.createIceServer())
        }
        if (list.isEmpty()) {
            list.add(
                PeerConnection.IceServer.builder("stun:stun.l.google.com:19302")
                    .createIceServer(),
            )
        }
        return list
    }

    private fun connectWs(url: String, roomId: String) {
        val client = OkHttpClient()
        val request = Request.Builder().url(url).build()
        ws = client.newWebSocket(
            request,
            object : WebSocketListener() {
                override fun onOpen(webSocket: WebSocket, response: Response) {
                    webSocket.send(
                        JSONObject().apply {
                            put("type", "join")
                            put("roomId", roomId)
                            put("role", "client")
                            put("displayName", android.os.Build.MODEL)
                        }.toString(),
                    )
                    onStatus("Signaling connected")
                }

                override fun onMessage(webSocket: WebSocket, text: String) {
                    scope.launch(Dispatchers.Main) {
                        handleSignalingMessage(JSONObject(text))
                    }
                }

                override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
                    val msg = t.message ?: "ws failure"
                    if (intentionallyDisconnected) return
                    onStatus("Connection lost: $msg")
                    scheduleReconnect()
                }

                override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
                    if (intentionallyDisconnected) return
                    onStatus("Connection closed")
                    scheduleReconnect()
                }
            },
        )
    }

    private fun scheduleReconnect() {
        if (intentionallyDisconnected) return
        if (!sessionStarted) return
        if (reconnectAttempts >= maxReconnectAttempts) {
            onError("Reconnect failed after $maxReconnectAttempts attempts")
            return
        }
        reconnectJob?.cancel()
        val delayMs = (1000L * (1 shl reconnectAttempts)).coerceAtMost(30_000L)
        reconnectAttempts++
        onStatus("Reconnecting… (attempt $reconnectAttempts)")
        reconnectJob = scope.launch(Dispatchers.Main) {
            delay(delayMs)
            if (!sessionStarted || intentionallyDisconnected) return@launch
            tryReconnect()
        }
    }

    private suspend fun tryReconnect() {
        try {
            closeConnection()
            initFactory()
            val ice = buildIceServers(currentIceServers)
            val rtcConfig = PeerConnection.RTCConfiguration(ice).apply {
                sdpSemantics = PeerConnection.SdpSemantics.UNIFIED_PLAN
            }
            val renderer = videoSink ?: run {
                onError("No renderer available for reconnect")
                return
            }
            pc = factory!!.createPeerConnection(
                rtcConfig,
                object : PeerConnection.Observer {
                    override fun onSignalingChange(p0: PeerConnection.SignalingState?) {}
                    override fun onIceConnectionChange(state: PeerConnection.IceConnectionState?) {
                        when (state) {
                            PeerConnection.IceConnectionState.FAILED -> {
                                onStatus("ICE connection failed")
                            }
                            PeerConnection.IceConnectionState.DISCONNECTED -> {
                                onStatus("ICE disconnected")
                            }
                            PeerConnection.IceConnectionState.CONNECTED -> {
                                reconnectAttempts = 0
                                onStatus("ICE connected")
                            }
                            else -> {}
                        }
                    }
                    override fun onIceConnectionReceivingChange(p0: Boolean) {}
                    override fun onIceGatheringChange(p0: PeerConnection.IceGatheringState?) {}
                    override fun onIceCandidate(candidate: IceCandidate?) {
                        candidate ?: return
                        sendSignal(
                            JSONObject().apply {
                                put("kind", "ice")
                                put("candidate", candidate.sdp)
                                put("sdpMid", candidate.sdpMid)
                                put("sdpMLineIndex", candidate.sdpMLineIndex)
                            },
                        )
                    }
                    override fun onIceCandidatesRemoved(p0: Array<out IceCandidate>?) {}
                    override fun onAddStream(p0: MediaStream?) {}
                    override fun onRemoveStream(p0: MediaStream?) {}
                    override fun onDataChannel(dc: DataChannel?) {
                        if (dc?.label() == "ctrl") {
                            ctrl = dc
                            dc.registerObserver(
                                object : DataChannel.Observer {
                                    override fun onBufferedAmountChange(p0: Long) {}
                                    override fun onStateChange() {}
                                    override fun onMessage(buffer: DataChannel.Buffer?) {}
                                },
                            )
                            onStatus("Control channel ready")
                        }
                    }
                    override fun onRenegotiationNeeded() {}
                    override fun onAddTrack(
                        receiver: RtpReceiver?,
                        streams: Array<out MediaStream>?,
                    ) {
                        val track = receiver?.track() as? VideoTrack ?: return
                        remoteVideo = track
                        track.setEnabled(true)
                        videoSink = renderer
                        track.addSink(renderer)
                        onStatus("Video playing")
                    }
                },
            )
            connectWs(currentSignalingUrl, currentRoomId)
        } catch (e: Exception) {
            onStatus("Reconnect failed: ${e.message}")
            scheduleReconnect()
        }
    }

    private fun handleSignalingMessage(msg: JSONObject) {
        when (msg.optString("type")) {
            "joined", "peer", "pong" -> { /* ignore */ }
            "error" -> onError(msg.optString("message"))
            "signal" -> {
                val payload = msg.getJSONObject("payload")
                when (payload.optString("kind")) {
                    "offer" -> handleOffer(payload)
                    "ice" -> handleRemoteIce(payload)
                }
            }
        }
    }

    private fun handleOffer(payload: JSONObject) {
        val sdp = payload.getString("sdp")
        val offer = SessionDescription(SessionDescription.Type.OFFER, sdp)
        pc!!.setRemoteDescription(
            object : org.webrtc.SdpObserver {
                override fun onCreateSuccess(p0: SessionDescription?) {}
                override fun onSetSuccess() {
                    remoteDescSet = true
                    drainPendingIce()
                    answer()
                }

                override fun onCreateFailure(p0: String?) {
                    onError("createSDP fail $p0")
                }

                override fun onSetFailure(p0: String?) {
                    onError("setRemote fail $p0")
                }
            },
            offer,
        )
    }

    private fun answer() {
        val constraints = MediaConstraints()
        pc!!.createAnswer(
            object : org.webrtc.SdpObserver {
                override fun onCreateSuccess(desc: SessionDescription?) {
                    desc ?: return
                    pc!!.setLocalDescription(
                        object : org.webrtc.SdpObserver {
                            override fun onCreateSuccess(p0: SessionDescription?) {}
                            override fun onSetSuccess() {
                                sendSignal(
                                    JSONObject().apply {
                                        put("kind", "answer")
                                        put("sdp", desc.description)
                                    },
                                )
                            }

                            override fun onCreateFailure(p0: String?) {}
                            override fun onSetFailure(p0: String?) {
                                onError("setLocal fail $p0")
                            }
                        },
                        desc,
                    )
                }

                override fun onSetSuccess() {}
                override fun onCreateFailure(p0: String?) {
                    onError("createAnswer $p0")
                }

                override fun onSetFailure(p0: String?) {}
            },
            constraints,
        )
    }

    private fun handleRemoteIce(payload: JSONObject) {
        val cand = payload.getString("candidate")
        val mid =
            if (payload.has("sdpMid") && !payload.isNull("sdpMid")) payload.getString("sdpMid") else null
        val line =
            if (payload.has("sdpMLineIndex") && !payload.isNull("sdpMLineIndex")) {
                payload.getInt("sdpMLineIndex")
            } else {
                0
            }
        val ice = IceCandidate(mid, line, cand)
        if (!remoteDescSet) {
            pendingRemoteIce.add(ice)
        } else {
            pc!!.addIceCandidate(ice)
        }
    }

    private fun drainPendingIce() {
        while (true) {
            val c = pendingRemoteIce.poll() ?: break
            pc!!.addIceCandidate(c)
        }
    }

    private fun sendSignal(payload: JSONObject) {
        val env = JSONObject().apply {
            put("type", "signal")
            put("payload", payload)
        }
        ws?.send(env.toString())
    }
}
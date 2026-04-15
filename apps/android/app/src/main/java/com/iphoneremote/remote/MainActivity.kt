package com.iphoneremote.remote

import android.os.Bundle
import androidx.activity.compose.setContent
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Keyboard
import androidx.compose.material3.Button
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView

import org.webrtc.RendererCommon
import org.webrtc.SurfaceViewRenderer

class MainActivity : androidx.activity.ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            MaterialTheme {
                Surface(
                    modifier = Modifier
                        .fillMaxSize()
                        .semantics { testTagsAsResourceId = true },
                ) {
                    RemoteScreen()
                }
            }
        }
    }
}

@Composable
private fun RemoteScreen() {
    val ctx = LocalContext.current
    val scope = rememberCoroutineScope()
    var signaling by remember { mutableStateOf("ws://10.0.2.2:8787") }
    var room by remember { mutableStateOf("demo") }
    var status by remember { mutableStateOf("") }
    var running by remember { mutableStateOf(false) }
    var videoView by remember { mutableStateOf<SurfaceViewRenderer?>(null) }
    var showInput by remember { mutableStateOf(false) }
    var inputText by remember { mutableStateOf("") }

    val session = remember {
        RemoteSession(
            ctx,
            scope,
            onStatus = { status = it },
            onError = { status = "Error: $it" },
        )
    }

    LaunchedEffect(videoView, running, signaling, room) {
        val v = videoView
        if (running && v != null) {
            session.start(signaling, room, v)
        }
    }

    DisposableEffect(Unit) {
        onDispose {
            session.dispose()
            try {
                videoView?.release()
            } catch (_: Exception) {
            }
        }
    }

    Column(modifier = Modifier.fillMaxSize()) {
        if (!running) {
            Column(
                modifier = Modifier
                    .padding(16.dp)
                    .testTag("e2e_login_root")
                    .semantics { contentDescription = "e2e_login_root" },
            ) {
                OutlinedTextField(
                    value = signaling,
                    onValueChange = { signaling = it },
                    label = { Text("Signaling WebSocket") },
                    modifier = Modifier
                        .fillMaxWidth()
                        .testTag("e2e_field_signaling")
                        .semantics { contentDescription = "e2e_field_signaling" },
                )
                OutlinedTextField(
                    value = room,
                    onValueChange = { room = it },
                    label = { Text("Room ID") },
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(top = 8.dp)
                        .testTag("e2e_field_room")
                        .semantics { contentDescription = "e2e_field_room" },
                )
                Button(
                    onClick = {
                        running = true
                        status = "Connecting\u2026"
                    },
                    modifier = Modifier
                        .padding(top = 16.dp)
                        .testTag("e2e_button_connect")
                        .semantics { contentDescription = "e2e_button_connect" },
                ) {
                    Text("Connect")
                }
                Text(
                    status,
                    modifier = Modifier
                        .padding(top = 16.dp)
                        .testTag("e2e_status_line")
                        .semantics { contentDescription = "e2e_status_line" },
                )
            }
        } else {
            Box(
                modifier = Modifier
                    .fillMaxSize()
                    .testTag("e2e_session_root")
                    .semantics { contentDescription = "e2e_session_root" },
            ) {
                AndroidView(
                    modifier = Modifier
                        .fillMaxSize()
                        .testTag("e2e_remote_surface")
                        .semantics { contentDescription = "e2e_remote_surface" }
                        .pointerInput(Unit) {
                            detectTapGestures(
                                onTap = { off: Offset ->
                                    val w = size.width.toFloat()
                                    val h = size.height.toFloat()
                                    val nx = (off.x / w).coerceIn(0f, 1f)
                                    val ny = (off.y / h).coerceIn(0f, 1f)
                                    session.sendControlJson(
                                        """{"v":1,"t":"down","x":$nx,"y":$ny,"b":0}""",
                                    )
                                    session.sendControlJson(
                                        """{"v":1,"t":"up","x":$nx,"y":$ny,"b":0}""",
                                    )
                                },
                                onLongPress = { off: Offset ->
                                    val w = size.width.toFloat()
                                    val h = size.height.toFloat()
                                    val nx = (off.x / w).coerceIn(0f, 1f)
                                    val ny = (off.y / h).coerceIn(0f, 1f)
                                    session.sendControlJson(
                                        """{"v":1,"t":"down","x":$nx,"y":$ny,"b":1}""",
                                    )
                                    session.sendControlJson(
                                        """{"v":1,"t":"up","x":$nx,"y":$ny,"b":1}""",
                                    )
                                },
                            )
                        }
                        .pointerInput(Unit) {
                            awaitPointerEventScope {
                                while (true) {
                                    val event = awaitPointerEvent(PointerEventPass.Initial)
                                    val changes = event.changes
                                    if (changes.isEmpty()) continue
                                    if (changes.size >= 2) {
                                        val c1 = changes.toList()[1]
                                        val dy = c1.previousPosition.y - c1.position.y
                                        val scrollTicks = (dy * 120 / size.height).toInt()
                                        if (scrollTicks != 0) {
                                            session.sendControlJson(
                                                """{"v":1,"t":"wheel","dx":0,"dy":$scrollTicks}""",
                                            )
                                        }
                                        changes.forEach { it.consume() }
                                    } else {
                                        val change = changes.first()
                                        if (change.pressed && change.previousPosition != change.position) {
                                            val w = size.width.toFloat()
                                            val h = size.height.toFloat()
                                            val nx = (change.position.x / w).coerceIn(0f, 1f)
                                            val ny = (change.position.y / h).coerceIn(0f, 1f)
                                            session.sendControlJson(
                                                """{"v":1,"t":"move","x":$nx,"y":$ny}""",
                                            )
                                            change.consume()
                                        }
                                    }
                                }
                            }
                        },
                    factory = { c ->
                        SurfaceViewRenderer(c).apply {
                            init(session.eglContext(), null)
                            setScalingType(RendererCommon.ScalingType.SCALE_ASPECT_FIT)
                            setEnableHardwareScaler(true)
                            videoView = this
                        }
                    },
                )

                Row(
                    modifier = Modifier
                        .align(Alignment.TopEnd)
                        .padding(8.dp),
                    horizontalArrangement = Arrangement.spacedBy(4.dp),
                ) {
                    IconButton(
                        onClick = { showInput = !showInput },
                        modifier = Modifier
                            .size(40.dp)
                            .background(Color(0x66000000), shape = MaterialTheme.shapes.small)
                            .testTag("e2e_button_keyboard")
                            .semantics { contentDescription = "e2e_button_keyboard" },
                    ) {
                        Icon(
                            Icons.Default.Keyboard,
                            contentDescription = "Keyboard",
                            tint = Color.White,
                        )
                    }
                    IconButton(
                        onClick = {
                            session.disconnect()
                            running = false
                            status = ""
                            videoView = null
                        },
                        modifier = Modifier
                            .size(40.dp)
                            .background(Color(0x66000000), shape = MaterialTheme.shapes.small)
                            .testTag("e2e_button_disconnect")
                            .semantics { contentDescription = "e2e_button_disconnect" },
                    ) {
                        Icon(
                            Icons.Default.Close,
                            contentDescription = "Disconnect",
                            tint = Color.White,
                        )
                    }
                }

                Text(
                    status,
                    modifier = Modifier
                        .align(Alignment.TopCenter)
                        .background(Color(0x88000000))
                        .padding(8.dp)
                        .testTag("e2e_session_status")
                        .semantics { contentDescription = "e2e_session_status" },
                    color = Color.White,
                )

                AnimatedVisibility(
                    visible = showInput,
                    enter = fadeIn(),
                    exit = fadeOut(),
                    modifier = Modifier
                        .align(Alignment.BottomCenter)
                        .background(Color(0xCC000000)),
                ) {
                    Column(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(8.dp),
                    ) {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .horizontalScroll(rememberScrollState()),
                            horizontalArrangement = Arrangement.spacedBy(4.dp),
                        ) {
                            val keys = listOf(
                                "Esc" to 27, "Tab" to 9, "Enter" to 13, "Bksp" to 8, "Del" to 46,
                                "\u2191" to 38, "\u2193" to 40, "\u2190" to 37, "\u2192" to 39,
                                "Home" to 36, "End" to 35, "PgUp" to 33, "PgDn" to 34,
                            )
                            for ((label, vk) in keys) {
                                ShortcutKey(label) {
                                    session.sendControlJson(ControlMessage.key(vk, true))
                                    session.sendControlJson(ControlMessage.key(vk, false))
                                }
                            }
                        }
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(top = 4.dp)
                                .horizontalScroll(rememberScrollState()),
                            horizontalArrangement = Arrangement.spacedBy(4.dp),
                        ) {
                            for (i in 1..12) {
                                val vk = 111 + i
                                ShortcutKey("F$i") {
                                    session.sendControlJson(ControlMessage.key(vk, true))
                                    session.sendControlJson(ControlMessage.key(vk, false))
                                }
                            }
                        }
                        OutlinedTextField(
                            value = inputText,
                            onValueChange = { newText ->
                                if (newText.length > inputText.length) {
                                    val added = newText.substring(inputText.length)
                                    session.sendControlJson(ControlMessage.text(added))
                                }
                                inputText = ""
                            },
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(top = 4.dp)
                                .testTag("e2e_text_input")
                                .semantics { contentDescription = "e2e_text_input" },
                            placeholder = { Text("Type text\u2026", color = Color.Gray) },
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun ShortcutKey(label: String, onClick: () -> Unit) {
    Button(
        onClick = onClick,
        modifier = Modifier.size(44.dp),
        contentPadding = PaddingValues(0.dp),
    ) {
        Text(label, fontSize = 10.sp)
    }
}
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Windows;

namespace RemoteHost;

/// <summary>WebSocket signaling + WebRTC host (offerer) with reconnect support.</summary>
public sealed class RemoteHostRunner : IDisposable
{
    private readonly HostOptions _opt;
    private readonly SessionStats _stats = new();
    private readonly InputInjector _injector = new();
    private ClientWebSocket? _ws;
    private RTCPeerConnection? _pc;
    private WindowsVideoEndPoint? _videoEndPoint;
    private VideoTestPatternSource? _testPattern;
    private DesktopVideoSession? _desktop;
    private RTCDataChannel? _dataChannel;
    private CancellationTokenSource? _runCts;
    private const int MaxReconnectAttempts = 5;
    private static readonly TimeSpan[] Backoff = { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(16) };

    public RemoteHostRunner(HostOptions opt)
    {
        _opt = opt;
    }

    public SessionStats Stats => _stats;

    public async Task RunAsync(CancellationToken ct)
    {
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _runCts.Token;
        var attempt = 0;

        while (!token.IsCancellationRequested)
        {
            try
            {
                await ConnectAndRunAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _stats.LastError = $"Connection lost: {ex.Message}";
                Console.WriteLine($"[host] {ex.Message}");
            }

            StopMedia();
            CleanupPeerConnection();
            CleanupWebSocket();

            if (token.IsCancellationRequested)
                break;

            if (attempt >= MaxReconnectAttempts)
            {
                Console.WriteLine($"[host] Max reconnect attempts ({MaxReconnectAttempts}) reached. Stopping.");
                break;
            }

            var delay = attempt < Backoff.Length ? Backoff[attempt] : Backoff[^1];
            Console.WriteLine($"[host] Reconnecting in {delay.TotalSeconds}s (attempt {attempt + 1}/{MaxReconnectAttempts})...");
            _stats.ConnectionState = "reconnecting";
            attempt++;

            try
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ConnectAndRunAsync(CancellationToken token)
    {
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(_opt.SignalingWs, token).ConfigureAwait(false);

        await SendJson(
            new Dictionary<string, object?>
            {
                ["type"] = "join",
                ["roomId"] = _opt.RoomId,
                ["role"] = "host",
                ["displayName"] = Environment.MachineName,
            },
            token).ConfigureAwait(false);

        _stats.ConnectionState = "connected";
        _stats.LastError = null;

        var peerReady = false;
        while (!token.IsCancellationRequested)
        {
            var msg = await ReceiveJson(token).ConfigureAwait(false);
            if (msg is null)
                break;

            var type = GetString(msg, "type");
            if (type == "joined")
            {
                if (GetBool(msg, "peerPresent") == true)
                    peerReady = true;
            }
            else if (type == "peer")
            {
                if (GetString(msg, "event") == "joined" && GetString(msg, "role") == "client")
                    peerReady = true;
            }
            else if (type == "signal")
            {
                await HandleRemoteSignal(msg, token).ConfigureAwait(false);
            }
            else if (type == "error")
            {
                _stats.LastError = GetString(msg, "message");
                throw new InvalidOperationException(_stats.LastError);
            }

            if (peerReady && _pc is null)
            {
                await StartWebRtcHostAsync(token).ConfigureAwait(false);
                peerReady = false;
            }
        }
    }

    private async Task StartWebRtcHostAsync(CancellationToken token)
    {
        var ice = BuildRtcConfiguration();
        _pc = new RTCPeerConnection(ice);

        _videoEndPoint = new WindowsVideoEndPoint(true);

        if (_opt.Video == VideoMode.Test)
        {
            _testPattern = new VideoTestPatternSource();
            _testPattern.OnVideoSourceRawSample += _videoEndPoint.ExternalVideoSourceRawSample;
            _videoEndPoint.OnVideoSourceEncodedSample += _pc.SendVideo;
            _pc.OnVideoFormatsNegotiated += formats =>
            {
                if (formats.Count > 0)
                    _videoEndPoint!.SetVideoSourceFormat(formats[0]);
            };
        }
        else
        {
            _videoEndPoint.OnVideoSourceEncodedSample += _pc.SendVideo;
            _pc.OnVideoFormatsNegotiated += formats =>
            {
                if (formats.Count > 0)
                    _videoEndPoint!.SetVideoSourceFormat(formats[0]);
            };
            _desktop = new DesktopVideoSession(_videoEndPoint);
        }

        _pc.onconnectionstatechange += state =>
        {
            _stats.ConnectionState = state.ToString();
            if (state == RTCPeerConnectionState.connected)
            {
                if (_opt.Video == VideoMode.Test)
                {
                    _ = _testPattern!.StartVideo();
                    _ = _videoEndPoint!.StartVideo();
                }
                else
                {
                    _ = _videoEndPoint!.StartVideo();
                    _desktop!.Start();
                }
            }
            else if (state is RTCPeerConnectionState.failed or RTCPeerConnectionState.closed)
            {
                StopMedia();
            }
        };

        _pc.oniceconnectionstatechange += iceState =>
        {
            _stats.IceState = iceState.ToString();
        };

        _pc.onicecandidate += (RTCIceCandidate? ice) =>
        {
            if (ice is null)
                return;
            var body = ice.candidate;
            var candLine = body.StartsWith(RTCIceCandidate.CANDIDATE_PREFIX, StringComparison.Ordinal)
                ? body
                : $"{RTCIceCandidate.CANDIDATE_PREFIX}:{body}";
            _ = SendSignalAsync(
                new Dictionary<string, object?>
                {
                    ["kind"] = "ice",
                    ["candidate"] = candLine,
                    ["sdpMid"] = ice.sdpMid,
                    ["sdpMLineIndex"] = (int)ice.sdpMLineIndex,
                },
                token);
        };

        var dcInit = new RTCDataChannelInit { ordered = true };
        _dataChannel = await _pc.createDataChannel("ctrl", dcInit).ConfigureAwait(false);
        _dataChannel.onopen += () => Console.WriteLine("[webrtc] ctrl open");
        _dataChannel.onmessage += OnDataChannelMessage;

        MediaStreamTrack videoTrack = new(
            _videoEndPoint.GetVideoSourceFormats(),
            MediaStreamStatusEnum.SendOnly);
        _pc.addTrack(videoTrack);

        var offer = _pc.createOffer(null);
        await _pc.setLocalDescription(offer).ConfigureAwait(false);

        await SendSignalAsync(
            new Dictionary<string, object?>
            {
                ["kind"] = "offer",
                ["sdp"] = offer.sdp,
            },
            token).ConfigureAwait(false);
    }

    private void OnDataChannelMessage(RTCDataChannel channel, DataChannelPayloadProtocols protocol, byte[] data)
    {
        try
        {
            var text = Encoding.UTF8.GetString(data);
            if (!ControlMessages.TryHandle(text, _injector, _stats))
                Console.WriteLine("[ctrl] unhandled: " + text);
        }
        catch (Exception ex)
        {
            _stats.LastError = ex.Message;
        }
    }

    private async Task HandleRemoteSignal(Dictionary<string, JsonElement> msg, CancellationToken token)
    {
        if (_pc is null)
            return;
        if (!msg.TryGetValue("payload", out var payloadEl))
            return;
        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            payloadEl.GetRawText());
        if (payload is null)
            return;

        var kind = GetString(payload, "kind");
        if (kind == "answer" && payload.TryGetValue("sdp", out var sdpEl))
        {
            var answer = new RTCSessionDescriptionInit
            {
                type = RTCSdpType.answer,
                sdp = sdpEl.GetString(),
            };
            var r = _pc.setRemoteDescription(answer);
            if (r != SetDescriptionResultEnum.OK)
                _stats.LastError = "setRemoteDescription answer: " + r;
        }
        else if (kind == "ice" && payload.TryGetValue("candidate", out var cEl))
        {
            var cand = cEl.GetString();
            if (string.IsNullOrEmpty(cand))
                return;
            ushort? mline = null;
            if (payload.TryGetValue("sdpMLineIndex", out var idx) && idx.ValueKind != JsonValueKind.Null)
                mline = idx.TryGetInt32(out var mi) ? (ushort)mi : idx.GetUInt16();

            var init = new RTCIceCandidateInit
            {
                candidate = cand,
                sdpMid = payload.TryGetValue("sdpMid", out var mid) && mid.ValueKind != JsonValueKind.Null
                    ? mid.GetString()
                    : null,
                sdpMLineIndex = mline,
            };
            _pc.addIceCandidate(init);
        }
    }

    private void StopMedia()
    {
        try
        {
            _desktop?.Stop();
            _desktop?.Dispose();
            _desktop = null;
        }
        catch { /* ignore */ }

        try
        {
            if (_testPattern is not null)
            {
                _testPattern.OnVideoSourceRawSample -= _videoEndPoint!.ExternalVideoSourceRawSample;
                _ = _testPattern.CloseVideo();
                _testPattern = null;
            }
        }
        catch { /* ignore */ }

        try
        {
            if (_videoEndPoint is not null)
            {
                _videoEndPoint.OnVideoSourceEncodedSample -= _pc!.SendVideo;
                _ = _videoEndPoint.CloseVideo();
                _videoEndPoint.Dispose();
                _videoEndPoint = null;
            }
        }
        catch { /* ignore */ }
    }

    private void CleanupPeerConnection()
    {
        try { _pc?.Close("reconnect"); } catch { /* ignore */ }
        try { _pc?.Dispose(); } catch { /* ignore */ }
        _pc = null;
        _dataChannel = null;
    }

    private void CleanupWebSocket()
    {
        try
        {
            if (_ws is not null && _ws.State == WebSocketState.Open)
                _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "reconnect", CancellationToken.None).GetAwaiter().GetResult();
        }
        catch { /* ignore */ }
        try { _ws?.Dispose(); } catch { /* ignore */ }
        _ws = null;
    }

    private RTCConfiguration BuildRtcConfiguration()
    {
        var list = new List<RTCIceServer>();
        foreach (var s in _opt.IceServers)
        {
            list.Add(
                new RTCIceServer
                {
                    urls = s.Urls,
                    username = s.Username,
                    credential = s.Credential,
                    credentialType = RTCIceCredentialType.password,
                });
        }
        if (list.Count == 0)
        {
            list.Add(new RTCIceServer { urls = "stun:stun.l.google.com:19302" });
        }
        return new RTCConfiguration { iceServers = list };
    }

    private async Task SendSignalAsync(Dictionary<string, object?> payload, CancellationToken token)
    {
        await SendJson(
            new Dictionary<string, object?> { ["type"] = "signal", ["payload"] = payload },
            token).ConfigureAwait(false);
    }

    private async Task SendJson(Dictionary<string, object?> obj, CancellationToken token)
    {
        if (_ws is null || _ws.State != WebSocketState.Open)
            return;
        var json = JsonSerializer.Serialize(obj);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, token).ConfigureAwait(false);
    }

    private async Task<Dictionary<string, JsonElement>?> ReceiveJson(CancellationToken token)
    {
        if (_ws is null)
            return null;
        var buffer = new byte[1024 * 64];
        var seg = new ArraySegment<byte>(buffer);
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await _ws.ReceiveAsync(seg, token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        var text = Encoding.UTF8.GetString(ms.ToArray());
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(text);
    }

    private static string? GetString(Dictionary<string, JsonElement> d, string key)
    {
        return d.TryGetValue(key, out var el) ? el.GetString() : null;
    }

    private static bool? GetBool(Dictionary<string, JsonElement> d, string key)
    {
        if (!d.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    public void Dispose()
    {
        try { _runCts?.Cancel(); } catch { /* ignore */ }
        StopMedia();
        CleanupPeerConnection();
        CleanupWebSocket();
        try { _runCts?.Dispose(); } catch { /* ignore */ }
    }
}
namespace RemoteHost;

public enum VideoMode
{
    Test,
    Desktop,
}

public sealed class HostOptions
{
    public required Uri SignalingWs { get; init; }
    public required string RoomId { get; init; }
    public int? ProbePort { get; init; }
    public VideoMode Video { get; init; } = VideoMode.Test;
    public List<RTCIceServerConfig> IceServers { get; init; } = new();
}

public sealed class RTCIceServerConfig
{
    public required string Urls { get; init; }
    public string? Username { get; init; }
    public string? Credential { get; init; }
}

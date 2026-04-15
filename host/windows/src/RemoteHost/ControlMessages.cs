using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteHost;

/// <summary>ctrl:v1 JSON messages from Android DataChannel.</summary>
public static class ControlMessages
{
    public const int Version = 1;

    public static bool TryHandle(string json, InputInjector injector, SessionStats stats)
    {
        CtrlMsg? m;
        try
        {
            m = JsonSerializer.Deserialize<CtrlMsg>(json, SerializerOptions);
        }
        catch
        {
            return false;
        }
        if (m is null || m.V != Version || string.IsNullOrEmpty(m.T))
            return false;

        switch (m.T)
        {
            case "move":
                if (m.X is null || m.Y is null) break;
                injector.MoveNormalized(m.X.Value, m.Y.Value);
                break;
            case "down":
                stats.Clicks++;
                if (m.X is null || m.Y is null) break;
                injector.ButtonDownNormalized(m.X.Value, m.Y.Value, m.B ?? 0);
                break;
            case "up":
                if (m.X is null || m.Y is null) break;
                injector.ButtonUpNormalized(m.X.Value, m.Y.Value, m.B ?? 0);
                break;
            case "wheel":
                injector.Wheel(m.Dx ?? 0, m.Dy ?? 0);
                break;
            case "key":
                stats.Keys++;
                if (m.K is null) break;
                injector.Key(m.K.Value, m.Down ?? true);
                break;
            case "text":
                if (!string.IsNullOrEmpty(m.S))
                    injector.Text(m.S);
                break;
            default:
                return false;
        }
        return true;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private sealed class CtrlMsg
    {
        [JsonPropertyName("v")] public int V { get; set; }
        [JsonPropertyName("t")] public string? T { get; set; }
        [JsonPropertyName("x")] public double? X { get; set; }
        [JsonPropertyName("y")] public double? Y { get; set; }
        [JsonPropertyName("b")] public int? B { get; set; }
        [JsonPropertyName("dx")] public int? Dx { get; set; }
        [JsonPropertyName("dy")] public int? Dy { get; set; }
        [JsonPropertyName("k")] public ushort? K { get; set; }
        [JsonPropertyName("down")] public bool? Down { get; set; }
        [JsonPropertyName("s")] public string? S { get; set; }
    }
}

public sealed class SessionStats
{
    public int Clicks { get; set; }
    public int Keys { get; set; }
    public string? LastError { get; set; }
    public string ConnectionState { get; set; } = "new";
    public string IceState { get; set; } = "new";
}

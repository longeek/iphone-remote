using System.Drawing;
using Xunit;

namespace RemoteHost.Tests;

public class MockInjector : InputInjector
{
    public List<(double X, double Y)> Moves { get; } = new();
    public List<(double X, double Y, int Button)> ButtonDowns { get; } = new();
    public List<(double X, double Y, int Button)> ButtonUps { get; } = new();
    public List<(int Dx, int Dy)> Wheels { get; } = new();
    public List<(ushort Vk, bool Down)> Keys { get; } = new();
    public List<string> Texts { get; } = new();

    public MockInjector() : base(new Rectangle(0, 0, 1920, 1080)) { }

    public override void MoveNormalized(double nx, double ny) => Moves.Add((nx, ny));
    public override void ButtonDownNormalized(double nx, double ny, int button) => ButtonDowns.Add((nx, ny, button));
    public override void ButtonUpNormalized(double nx, double ny, int button) => ButtonUps.Add((nx, ny, button));
    public override void Wheel(int dx, int dy) => Wheels.Add((dx, dy));
    public override void Key(ushort virtualKey, bool down) => Keys.Add((virtualKey, down));
    public override void Text(string s) => Texts.Add(s);
}

public class ControlMessagesTest
{
    private readonly MockInjector _injector = new();
    private readonly SessionStats _stats = new();

    [Fact]
    public void TryHandle_MoveMessage()
    {
        var json = @"{""v"":1,""t"":""move"",""x"":0.5,""y"":0.25}";
        Assert.True(ControlMessages.TryHandle(json, _injector, _stats));
        Assert.Single(_injector.Moves);
        Assert.Equal(0.5, _injector.Moves[0].X, 3);
        Assert.Equal(0.25, _injector.Moves[0].Y, 3);
    }

    [Fact]
    public void TryHandle_DownMessage_LeftButton()
    {
        var json = @"{""v"":1,""t"":""down"",""x"":0.1,""y"":0.2,""b"":0}";
        Assert.True(ControlMessages.TryHandle(json, _injector, _stats));
        Assert.Single(_injector.ButtonDowns);
        Assert.Equal(0, _injector.ButtonDowns[0].Button);
    }

    [Fact]
    public void TryHandle_DownMessage_RightButton()
    {
        var json = @"{""v"":1,""t"":""down"",""x"":0.3,""y"":0.4,""b"":1}";
        Assert.True(ControlMessages.TryHandle(json, _injector, _stats));
        Assert.Equal(1, _injector.ButtonDowns[0].Button);
    }

    [Fact]
    public void TryHandle_UpMessage()
    {
        var json = @"{""v"":1,""t"":""up"",""x"":0.5,""y"":0.5,""b"":0}";
        Assert.True(ControlMessages.TryHandle(json, _injector, _stats));
        Assert.Single(_injector.ButtonUps);
    }

    [Fact]
    public void TryHandle_DownIncrementsClicks()
    {
        var json = @"{""v"":1,""t"":""down"",""x"":0.1,""y"":0.2,""b"":0}";
        ControlMessages.TryHandle(json, _injector, _stats);
        Assert.Equal(1, _stats.Clicks);
    }

    [Fact]
    public void TryHandle_WheelMessage()
    {
        var json = @"{""v"":1,""t"":""wheel"",""dx"":1,""dy"":-3}";
        Assert.True(ControlMessages.TryHandle(json, _injector, _stats));
        Assert.Single(_injector.Wheels);
        Assert.Equal(1, _injector.Wheels[0].Dx);
        Assert.Equal(-3, _injector.Wheels[0].Dy);
    }

    [Fact]
    public void TryHandle_WheelDefaultsToZero()
    {
        var json = @"{""v"":1,""t"":""wheel""}";
        Assert.True(ControlMessages.TryHandle(json, _injector, _stats));
        Assert.Single(_injector.Wheels);
        Assert.Equal(0, _injector.Wheels[0].Dx);
        Assert.Equal(0, _injector.Wheels[0].Dy);
    }

    [Fact]
    public void TryHandle_KeyDown()
    {
        var json = @"{""v"":1,""t"":""key"",""k"":65}";
        Assert.True(ControlMessages.TryHandle(json, _injector, _stats));
        Assert.Single(_injector.Keys);
        Assert.Equal(65, _injector.Keys[0].Vk);
        Assert.True(_injector.Keys[0].Down);
        Assert.Equal(1, _stats.Keys);
    }

    [Fact]
    public void TryHandle_KeyUp()
    {
        var json = @"{""v"":1,""t"":""key"",""k"":65,""down"":false}";
        Assert.True(ControlMessages.TryHandle(json, _injector, _stats));
        Assert.False(_injector.Keys[0].Down);
    }

    [Fact]
    public void TryHandle_TextMessage()
    {
        var json = @"{""v"":1,""t"":""text"",""s"":""hello""}";
        Assert.True(ControlMessages.TryHandle(json, _injector, _stats));
        Assert.Single(_injector.Texts);
        Assert.Equal("hello", _injector.Texts[0]);
    }

    [Fact]
    public void TryHandle_TextEmptyString()
    {
        var json = @"{""v"":1,""t"":""text"",""s"":""""}";
        Assert.False(ControlMessages.TryHandle(json, _injector, _stats));
    }

    [Fact]
    public void TryHandle_TextNull()
    {
        var json = @"{""v"":1,""t"":""text""}";
        Assert.False(ControlMessages.TryHandle(json, _injector, _stats));
    }

    [Fact]
    public void TryHandle_WrongVersion()
    {
        var json = @"{""v"":2,""t"":""move"",""x"":0.5,""y"":0.5}";
        Assert.False(ControlMessages.TryHandle(json, _injector, _stats));
    }

    [Fact]
    public void TryHandle_InvalidJson()
    {
        Assert.False(ControlMessages.TryHandle("not json", _injector, _stats));
    }

    [Fact]
    public void TryHandle_MissingType()
    {
        var json = @"{""v"":1,""x"":0.5,""y"":0.5}";
        Assert.False(ControlMessages.TryHandle(json, _injector, _stats));
    }

    [Fact]
    public void TryHandle_UnknownType()
    {
        var json = @"{""v"":1,""t"":""unknown""}";
        Assert.False(ControlMessages.TryHandle(json, _injector, _stats));
    }

    [Fact]
    public void TryHandle_MoveWithoutCoordinates()
    {
        var json = @"{""v"":1,""t"":""move""}";
        Assert.True(ControlMessages.TryHandle(json, _injector, _stats));
        Assert.Empty(_injector.Moves);
    }

    [Fact]
    public void TryHandle_DownWithoutCoordinates()
    {
        var json = @"{""v"":1,""t"":""down"",""b"":0}";
        Assert.True(ControlMessages.TryHandle(json, _injector, _stats));
        Assert.Empty(_injector.ButtonDowns);
        Assert.Equal(1, _stats.Clicks);
    }

    [Fact]
    public void TryHandle_UpWithoutCoordinates()
    {
        var json = @"{""v"":1,""t"":""up"",""b"":0}";
        Assert.True(ControlMessages.TryHandle(json, _injector, _stats));
        Assert.Empty(_injector.ButtonUps);
    }

    [Fact]
    public void TryHandle_KeyWithoutVk()
    {
        var json = @"{""v"":1,""t"":""key""}";
        Assert.True(ControlMessages.TryHandle(json, _injector, _stats));
        Assert.Empty(_injector.Keys);
        Assert.Equal(0, _stats.Keys);
    }

    [Fact]
    public void TryHandle_MultipleDownClicks()
    {
        for (int i = 0; i < 5; i++)
        {
            ControlMessages.TryHandle(
                @"{""v"":1,""t"":""down"",""x"":0.1,""y"":0.2,""b"":0}",
                _injector, _stats);
        }
        Assert.Equal(5, _stats.Clicks);
    }

    [Fact]
    public void TryHandle_TextWithSpecialChars()
    {
        var json = @"{""v"":1,""t"":""text"",""s"":""a\nb""}";
        Assert.True(ControlMessages.TryHandle(json, _injector, _stats));
        Assert.Equal("a\nb", _injector.Texts[0]);
    }

    [Fact]
    public void TryHandle_CaseInsensitiveProperties()
    {
        var json = @"{""V"":1,""T"":""move"",""X"":0.5,""Y"":0.5}";
        Assert.True(ControlMessages.TryHandle(json, _injector, _stats));
        Assert.Single(_injector.Moves);
    }

    [Fact]
    public void TryHandle_DownButtonDefaultsToZero()
    {
        var json = @"{""v"":1,""t"":""down"",""x"":0.1,""y"":0.2}";
        Assert.True(ControlMessages.TryHandle(json, _injector, _stats));
        Assert.Equal(0, _injector.ButtonDowns[0].Button);
    }

    [Fact]
    public void TryHandle_DownButtonTwo()
    {
        var json = @"{""v"":1,""t"":""down"",""x"":0.1,""y"":0.2,""b"":2}";
        Assert.True(ControlMessages.TryHandle(json, _injector, _stats));
        Assert.Equal(2, _injector.ButtonDowns[0].Button);
    }

    [Fact]
    public void TryHandle_DownKeyIncrementsKeyCounter()
    {
        var json = @"{""v"":1,""t"":""key"",""k"":65}";
        ControlMessages.TryHandle(json, _injector, _stats);
        Assert.Equal(1, _stats.Keys);
    }

    [Fact]
    public void TryHandle_MultipleKeyEvents()
    {
        ControlMessages.TryHandle(@"{""v"":1,""t"":""key"",""k"":65,""down"":true}", _injector, _stats);
        ControlMessages.TryHandle(@"{""v"":1,""t"":""key"",""k"":65,""down"":false}", _injector, _stats);
        Assert.Equal(2, _stats.Keys);
    }

    [Fact]
    public void TryHandle_VersionOneOnly()
    {
        var json0 = @"{""v"":0,""t"":""move"",""x"":0.5,""y"":0.5}";
        Assert.False(ControlMessages.TryHandle(json0, _injector, _stats));
    }

    [Fact]
    public void TryHandle_NullJson()
    {
        Assert.False(ControlMessages.TryHandle(null!, _injector, _stats));
    }

    [Fact]
    public void TryHandle_VersionConstant()
    {
        Assert.Equal(1, ControlMessages.Version);
    }

    [Fact]
    public void TryHandle_UpButtonOne()
    {
        var json = @"{""v"":1,""t"":""up"",""x"":0.5,""y"":0.5,""b"":1}";
        Assert.True(ControlMessages.TryHandle(json, _injector, _stats));
        Assert.Equal(1, _injector.ButtonUps[0].Button);
    }

    [Fact]
    public void TryHandle_UpButtonTwo()
    {
        var json = @"{""v"":1,""t"":""up"",""x"":0.5,""y"":0.5,""b"":2}";
        Assert.True(ControlMessages.TryHandle(json, _injector, _stats));
        Assert.Equal(2, _injector.ButtonUps[0].Button);
    }

    [Fact]
    public void TryHandle_UpButtonDefaultsToZero()
    {
        var json = @"{""v"":1,""t"":""up"",""x"":0.5,""y"":0.5}";
        Assert.True(ControlMessages.TryHandle(json, _injector, _stats));
        Assert.Equal(0, _injector.ButtonUps[0].Button);
    }

    [Fact]
    public void TryHandle_TextWithUnicode()
    {
        var json = @"{""v"":1,""t"":""text"",""s"":""\u4f60\u597d""}";
        Assert.True(ControlMessages.TryHandle(json, _injector, _stats));
        Assert.Equal("\u4f60\u597d", _injector.Texts[0]);
    }

    [Fact]
    public void TryHandle_MoveWithValues()
    {
        var json = @"{""v"":1,""t"":""move"",""x"":0.0,""y"":1.0}";
        Assert.True(ControlMessages.TryHandle(json, _injector, _stats));
        Assert.Equal(0.0, _injector.Moves[0].X, 3);
        Assert.Equal(1.0, _injector.Moves[0].Y, 3);
    }

    [Fact]
    public void TryHandle_WheelOnlyDx()
    {
        var json = @"{""v"":1,""t"":""wheel"",""dx"":5}";
        Assert.True(ControlMessages.TryHandle(json, _injector, _stats));
        Assert.Equal(5, _injector.Wheels[0].Dx);
        Assert.Equal(0, _injector.Wheels[0].Dy);
    }

    [Fact]
    public void TryHandle_WheelOnlyDy()
    {
        var json = @"{""v"":1,""t"":""wheel"",""dy"":-5}";
        Assert.True(ControlMessages.TryHandle(json, _injector, _stats));
        Assert.Equal(0, _injector.Wheels[0].Dx);
        Assert.Equal(-5, _injector.Wheels[0].Dy);
    }

    [Fact]
    public void TryHandle_KeyDownDefaultsToTrue()
    {
        var json = @"{""v"":1,""t"":""key"",""k"":27}";
        Assert.True(ControlMessages.TryHandle(json, _injector, _stats));
        Assert.True(_injector.Keys[0].Down);
    }

    [Fact]
    public void TryHandle_NumberAsString()
    {
        var json = @"{""v"":1,""t"":""move"",""x"":""0.5"",""y"":""0.25""}";
        Assert.True(ControlMessages.TryHandle(json, _injector, _stats));
        Assert.Single(_injector.Moves);
        Assert.Equal(0.5, _injector.Moves[0].X, 3);
        Assert.Equal(0.25, _injector.Moves[0].Y, 3);
    }

    [Fact]
    public void TryHandle_DownNullT()
    {
        var json = @"{""v"":1,""t"":null}";
        Assert.False(ControlMessages.TryHandle(json, _injector, _stats));
    }

    [Fact]
    public void TryHandle_EmptyT()
    {
        var json = @"{""v"":1,""t"":""""}";
        Assert.False(ControlMessages.TryHandle(json, _injector, _stats));
    }

    [Fact]
    public void TryHandle_NullV()
    {
        var json = @"{""v"":null,""t"":""move""}";
        Assert.False(ControlMessages.TryHandle(json, _injector, _stats));
    }
}

public class MockInjectorTest
{
    [Fact]
    public void MockInjector_CapturesAllOperations()
    {
        var injector = new MockInjector();
        injector.MoveNormalized(0.5, 0.5);
        injector.ButtonDownNormalized(0.1, 0.2, 1);
        injector.ButtonUpNormalized(0.1, 0.2, 1);
        injector.Wheel(2, -3);
        injector.Key(27, true);
        injector.Key(27, false);
        injector.Text("hello");

        Assert.Single(injector.Moves);
        Assert.Single(injector.ButtonDowns);
        Assert.Single(injector.ButtonUps);
        Assert.Single(injector.Wheels);
        Assert.Equal(2, injector.Keys.Count);
        Assert.Single(injector.Texts);
    }

    [Fact]
    public void MockInjector_MultipleMoves()
    {
        var injector = new MockInjector();
        for (int i = 0; i < 100; i++)
        {
            injector.MoveNormalized(i / 100.0, i / 100.0);
        }
        Assert.Equal(100, injector.Moves.Count);
    }
}
{
    [Fact]
    public void SessionStats_DefaultValues()
    {
        var stats = new SessionStats();
        Assert.Equal(0, stats.Clicks);
        Assert.Equal(0, stats.Keys);
        Assert.Null(stats.LastError);
        Assert.Equal("new", stats.ConnectionState);
        Assert.Equal("new", stats.IceState);
    }

    [Fact]
    public void SessionStats_SetProperties()
    {
        var stats = new SessionStats
        {
            Clicks = 10,
            Keys = 5,
            LastError = "test error",
            ConnectionState = "connected",
            IceState = "connected"
        };
        Assert.Equal(10, stats.Clicks);
        Assert.Equal(5, stats.Keys);
        Assert.Equal("test error", stats.LastError);
        Assert.Equal("connected", stats.ConnectionState);
        Assert.Equal("connected", stats.IceState);
    }
}

public class HostOptionsTest
{
    [Fact]
    public void HostOptions_DefaultValues()
    {
        var opt = new HostOptions
        {
            SignalingWs = new Uri("ws://localhost:8787"),
            RoomId = "test"
        };
        Assert.Equal(VideoMode.Test, opt.Video);
        Assert.Empty(opt.IceServers);
        Assert.Null(opt.ProbePort);
    }

    [Fact]
    public void HostOptions_AllPropertiesSet()
    {
        var opt = new HostOptions
        {
            SignalingWs = new Uri("ws://example.com:8787"),
            RoomId = "myroom",
            ProbePort = 18080,
            Video = VideoMode.Desktop,
            IceServers = new List<RTCIceServerConfig>
            {
                new() { Urls = "stun:stun.l.google.com:19302" },
                new() { Urls = "turn:example.com:3478", Username = "user", Credential = "pass" },
            }
        };
        Assert.Equal("myroom", opt.RoomId);
        Assert.Equal(18080, opt.ProbePort);
        Assert.Equal(VideoMode.Desktop, opt.Video);
        Assert.Equal(2, opt.IceServers.Count);
    }
}
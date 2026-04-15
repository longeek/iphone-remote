using Xunit;

namespace RemoteHost.Tests;

public class ArgParserTest
{
    [Fact]
    public void Parse_DefaultValues()
    {
        var opt = ArgParser.Parse(Array.Empty<string>());
        Assert.Equal("ws://127.0.0.1:8787", opt.SignalingWs.ToString());
        Assert.Equal("default", opt.RoomId);
        Assert.Null(opt.ProbePort);
        Assert.Equal(VideoMode.Test, opt.Video);
        Assert.Empty(opt.IceServers);
    }

    [Fact]
    public void Parse_SignalingUrl()
    {
        var opt = ArgParser.Parse(new[] { "--signaling", "ws://example.com:9000" });
        Assert.Equal("ws://example.com:9000/", opt.SignalingWs.ToString());
    }

    [Fact]
    public void Parse_RoomId()
    {
        var opt = ArgParser.Parse(new[] { "--room", "myroom" });
        Assert.Equal("myroom", opt.RoomId);
    }

    [Fact]
    public void Parse_ProbePort()
    {
        var opt = ArgParser.Parse(new[] { "--probe", "18080" });
        Assert.Equal(18080, opt.ProbePort);
    }

    [Fact]
    public void Parse_VideoDesktop()
    {
        var opt = ArgParser.Parse(new[] { "--video", "desktop" });
        Assert.Equal(VideoMode.Desktop, opt.Video);
    }

    [Fact]
    public void Parse_VideoTest()
    {
        var opt = ArgParser.Parse(new[] { "--video", "test" });
        Assert.Equal(VideoMode.Test, opt.Video);
    }

    [Fact]
    public void Parse_VideoUnknownDefaultsToTest()
    {
        var opt = ArgParser.Parse(new[] { "--video", "unknown" });
        Assert.Equal(VideoMode.Test, opt.Video);
    }

    [Fact]
    public void Parse_AllOptions()
    {
        var opt = ArgParser.Parse(new[]
        {
            "--signaling", "ws://host:1234",
            "--room", "testroom",
            "--probe", "9999",
            "--video", "desktop",
        });
        Assert.Equal("ws://host:1234/", opt.SignalingWs.ToString());
        Assert.Equal("testroom", opt.RoomId);
        Assert.Equal(9999, opt.ProbePort);
        Assert.Equal(VideoMode.Desktop, opt.Video);
    }

    [Fact]
    public void Parse_MissingSignalingValue_Throws()
    {
        Assert.Throws<ArgumentException>(() => ArgParser.Parse(new[] { "--signaling" }));
    }

    [Fact]
    public void Parse_MissingRoomValue_Throws()
    {
        Assert.Throws<ArgumentException>(() => ArgParser.Parse(new[] { "--room" }));
    }

    [Fact]
    public void Parse_MissingProbeValue_Throws()
    {
        Assert.Throws<ArgumentException>(() => ArgParser.Parse(new[] { "--probe" }));
    }

    [Fact]
    public void Parse_MissingVideoValue_Throws()
    {
        Assert.Throws<ArgumentException>(() => ArgParser.Parse(new[] { "--video" }));
    }

    [Fact]
    public void Parse_ProbePortIsInt()
    {
        var opt = ArgParser.Parse(new[] { "--probe", "8080" });
        Assert.Equal(8080, opt.ProbePort);
    }
}
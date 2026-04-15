using System.Net;
using Xunit;

namespace RemoteHost.Tests;

public class ProbeServerTest : IDisposable
{
    private readonly ProbeServer _server;
    private readonly int _port;
    private readonly HttpClient _http;

    public ProbeServerTest()
    {
        _port = FindFreePort();
        _server = new ProbeServer(_port);
        _http = new HttpClient();
    }

    public void Dispose()
    {
        _server.Dispose();
        _http.Dispose();
    }

    private static int FindFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsOk()
    {
        var response = await _http.GetAsync($"http://127.0.0.1:{_port}/health");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("ok", body);
    }

    [Fact]
    public async Task StatsEndpoint_ReturnsJson()
    {
        _server.SetStatsProvider(() => new SessionStats
        {
            ConnectionState = "connected",
            IceState = "connected",
            Clicks = 5,
            Keys = 3,
            LastError = null
        });

        var response = await _http.GetAsync($"http://127.0.0.1:{_port}/stats");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("connected", body);
        Assert.Contains("\"clicks\":5", body);
        Assert.Contains("\"keys\":3", body);
    }

    [Fact]
    public async Task StatsEndpoint_IncludesError()
    {
        _server.SetStatsProvider(() => new SessionStats
        {
            ConnectionState = "new",
            IceState = "new",
            LastError = "something failed"
        });

        var response = await _http.GetAsync($"http://127.0.0.1:{_port}/stats");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("something failed", body);
    }

    [Fact]
    public async Task UnknownPath_Returns404()
    {
        var response = await _http.GetAsync($"http://127.0.0.1:{_port}/unknown");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Stats_ReflectsClickAndKeyUpdates()
    {
        var stats = new SessionStats();
        _server.SetStatsProvider(() => stats);

        stats.Clicks = 10;
        stats.Keys = 7;

        var response = await _http.GetAsync($"http://127.0.0.1:{_port}/stats");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"clicks\":10", body);
        Assert.Contains("\"keys\":7", body);
    }

    [Fact]
    public async Task Stats_DefaultProvider_ReturnsEmptyObject()
    {
        var response = await _http.GetAsync($"http://127.0.0.1:{_port}/stats");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("{}", body.Trim());
    }

    [Fact]
    public async Task Stats_IncludesLastErrorWhenPresent()
    {
        _server.SetStatsProvider(() => new SessionStats
        {
            LastError = "test error msg"
        });

        var response = await _http.GetAsync($"http://127.0.0.1:{_port}/stats");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("test error msg", body);
    }

    [Fact]
    public async Task Stats_IncludesNullLastError()
    {
        _server.SetStatsProvider(() => new SessionStats { LastError = null });

        var response = await _http.GetAsync($"http://127.0.0.1:{_port}/stats");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("lastError", body);
    }

    [Fact]
    public async Task HealthEndpoint_Returns200StatusCode()
    {
        var response = await _http.GetAsync($"http://127.0.0.1:{_port}/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task StatsEndpoint_Returns200StatusCode()
    {
        _server.SetStatsProvider(() => new SessionStats());

        var response = await _http.GetAsync($"http://127.0.0.1:{_port}/stats");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RootPath_Returns404()
    {
        var response = await _http.GetAsync($"http://127.0.0.1:{_port}/");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    }
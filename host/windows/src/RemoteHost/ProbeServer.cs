using System.Net;
using System.Text;
using System.Text.Json;

namespace RemoteHost;

/// <summary>Localhost-only HTTP probe for E2E and health checks.</summary>
public sealed class ProbeServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Task _loop;
    private readonly CancellationTokenSource _cts = new();
    private Func<string> _statsJson = () => "{}";

    public ProbeServer(int port)
    {
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _loop = Task.Run(RunAsync);
    }

    public void SetStatsProvider(Func<SessionStats> stats)
    {
        _statsJson = () =>
        {
            var s = stats();
            return JsonSerializer.Serialize(new
            {
                connectionState = s.ConnectionState,
                iceState = s.IceState,
                clicks = s.Clicks,
                keys = s.Keys,
                lastError = s.LastError,
            });
        };
    }

    private async Task RunAsync()
    {
        _listener.Start();
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch when (_cts.IsCancellationRequested)
            {
                break;
            }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            byte[] body;
            if (path == "/health")
            {
                body = "ok"u8.ToArray();
                ctx.Response.StatusCode = 200;
            }
            else if (path == "/stats")
            {
                body = Encoding.UTF8.GetBytes(_statsJson());
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
            }
            else
            {
                body = "not found"u8.ToArray();
                ctx.Response.StatusCode = 404;
            }
            ctx.Response.ContentLength64 = body.Length;
            ctx.Response.OutputStream.Write(body, 0, body.Length);
            ctx.Response.Close();
        }
        catch
        {
            try { ctx.Response.Abort(); } catch { /* ignore */ }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { /* ignore */ }
        _listener.Close();
        try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        _cts.Dispose();
    }
}

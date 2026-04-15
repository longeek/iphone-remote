using System.Collections.Generic;
using SIPSorcery.Net;

namespace RemoteHost;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var opt = ArgParser.Parse(args);
            var iceFromEnv = Environment.GetEnvironmentVariable("ICE_SERVERS");
            if (!string.IsNullOrWhiteSpace(iceFromEnv))
            {
                foreach (var part in iceFromEnv.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    try
                    {
                        opt.IceServers.Add(ToConfig(RTCIceServer.Parse(part)));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[ice] skip '" + part + "': " + ex.Message);
                    }
                }
            }

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            ProbeServer? probe = null;
            try
            {
                if (opt.ProbePort is int port)
                    probe = new ProbeServer(port);

                using var runner = new RemoteHostRunner(opt);
                probe?.SetStatsProvider(() => runner.Stats);

                Console.WriteLine($"[host] signaling={opt.SignalingWs} room={opt.RoomId} video={opt.Video}");
                await runner.RunAsync(cts.Token).ConfigureAwait(false);
                return 0;
            }
            finally
            {
                probe?.Dispose();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[fatal] " + ex);
            return 1;
        }
    }

    private static RTCIceServerConfig ToConfig(RTCIceServer s)
    {
        return new RTCIceServerConfig
        {
            Urls = s.urls ?? "",
            Username = s.username,
            Credential = s.credential,
        };
    }
}
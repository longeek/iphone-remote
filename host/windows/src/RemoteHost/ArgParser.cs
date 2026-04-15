using System.Collections.Generic;

namespace RemoteHost;

public static class ArgParser
{
    public static HostOptions Parse(string[] args)
    {
        Uri? signaling = null;
        string room = "default";
        int? probe = null;
        var video = VideoMode.Test;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--signaling":
                    signaling = new Uri(RequireArg(args, ref i));
                    break;
                case "--room":
                    room = RequireArg(args, ref i);
                    break;
                case "--probe":
                    probe = int.Parse(RequireArg(args, ref i));
                    break;
                case "--video":
                    video = RequireArg(args, ref i).ToLowerInvariant() switch
                    {
                        "desktop" => VideoMode.Desktop,
                        _ => VideoMode.Test,
                    };
                    break;
            }
        }

        signaling ??= new Uri("ws://127.0.0.1:8787");

        return new HostOptions
        {
            SignalingWs = signaling,
            RoomId = room,
            ProbePort = probe,
            Video = video,
            IceServers = new List<RTCIceServerConfig>(),
        };
    }

    private static string RequireArg(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException("Missing value after " + args[i]);
        return args[++i];
    }
}
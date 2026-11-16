using System.IO.Pipes;
using System.Text;

namespace Clamshield.Services;

/// <summary>Single-instance forwarding over a per-user named pipe (cross-platform).</summary>
public static class SingleInstance
{
    static readonly string PipeName = "clamshield-" + new string((Environment.UserName ?? "user")
        .Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    static CancellationTokenSource? _cts;

    /// <summary>Send args to a running instance. True = delivered (caller should exit).</summary>
    public static bool TrySendToRunning(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(400);
            using var w = new StreamWriter(client, new UTF8Encoding(false));
            foreach (var a in args) w.WriteLine(a.Replace("\r", "").Replace("\n", " "));
            w.WriteLine(); // empty line terminates
            w.Flush();
            return true;
        }
        catch { return false; }
    }

    /// <summary>Start the listener loop; onMessage runs on a background thread.</summary>
    public static void StartServer(Action<string[]> onMessage)
    {
        Stop();
        var cts = _cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(cts.Token);
                    var args = new List<string>();
                    using var r = new StreamReader(server, Encoding.UTF8);
                    string? line;
                    while ((line = await r.ReadLineAsync(cts.Token)) != null && line.Length > 0) args.Add(line);
                    try { onMessage(args.ToArray()); } catch { }
                }
                catch (OperationCanceledException) { break; }
                catch { try { await Task.Delay(200, cts.Token); } catch { break; } }
            }
        });
    }

    public static void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        _cts = null;
    }

    /// <summary>Existing files/dirs listed after --scan (stops at the next "--" arg).</summary>
    public static string[] ParseScanPaths(string[] args)
    {
        var res = new List<string>();
        int i = Array.IndexOf(args, "--scan");
        if (i < 0) return Array.Empty<string>();
        for (i++; i < args.Length && !args[i].StartsWith("--"); i++)
            if (File.Exists(args[i]) || Directory.Exists(args[i])) res.Add(args[i]);
        return res.ToArray();
    }

    public static bool HasFlag(string[] args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
}

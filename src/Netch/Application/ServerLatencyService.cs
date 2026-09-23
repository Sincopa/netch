using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Netch.Models;
using Netch.Servers;

namespace Netch.Application;

/// <summary>Tests an endpoint through an isolated local core, without changing OS routes or drivers.</summary>
public static class ServerLatencyService
{
    public static async Task<int> MeasureHttpAsync(Server server, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Global.Settings.RequestTimeout);
        var token = timeout.Token;
        var core = Path.Combine(Global.NetchDir, "bin", "xray.exe");
        if (!File.Exists(core)) throw new MessageException("bin\\xray.exe file not found!");
        var directory = Path.Combine(Global.NetchDir, "data", "latency");
        Directory.CreateDirectory(directory);
        var configPath = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
        using var process = new Process();
        Task? stdout = null, stderr = null;
        var started = false;
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var config = await XrayProfileConfig.GenerateAsync(server, port, "127.0.0.1");
            await File.WriteAllTextAsync(configPath, config.ToJsonString(), token);
            process.StartInfo = new ProcessStartInfo(core)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(core)!, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { "run", "-c", configPath }) process.StartInfo.ArgumentList.Add(argument);
            started = process.Start();
            Global.Job.AddProcess(process);
            stdout = process.StandardOutput.ReadToEndAsync();
            stderr = process.StandardError.ReadToEndAsync();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (process.HasExited) return -1;
                using var socket = new TcpClient();
                try { await socket.ConnectAsync(IPAddress.Loopback, port, token); break; }
                catch (SocketException) { await Task.Delay(50, token); }
            }
            return await MeasureThroughProxyAsync(new Uri($"socks5://127.0.0.1:{port}"), Global.Settings.LatencyTestUrl, token);
        }
        finally
        {
            if (started && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
            if (stdout is not null && stderr is not null) await Task.WhenAll(stdout, stderr);
            File.Delete(configPath);
        }
    }

    public static async Task<int> MeasureThroughProxyAsync(Uri proxy, string url, CancellationToken token, NetworkCredential? credentials = null)
    {
        using var handler = new SocketsHttpHandler { Proxy = new WebProxy(proxy) { Credentials = credentials }, UseProxy = true, AllowAutoRedirect = true };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var watch = Stopwatch.StartNew();
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        return Math.Max(1, (int)Math.Ceiling(watch.Elapsed.TotalMilliseconds));
    }
}

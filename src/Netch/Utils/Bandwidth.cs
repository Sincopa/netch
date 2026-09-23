using System.Diagnostics;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.VisualStudio.Threading;
using Netch.Controllers;

namespace Netch.Utils;

public static class Bandwidth
{
    public static ulong received;
    public static ulong sent;
    public static TraceEventSession? tSession;

    private static readonly string[] Suffix = { "B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB" };

    /// <summary>
    ///     计算流量
    /// </summary>
    /// <param name="d"></param>
    /// <returns>带单位的流量字符串</returns>
    public static string Compute(ulong d)
    {
        const double step = 1024.00;

        byte level = 0;
        double? size = null;
        while ((size ?? d) > step)
        {
            if (level >= 6) // Suffix.Length - 1
                break;

            level++;
            size = (size ?? d) / step;
        }

        return $@"{size ?? 0:0.##} {Suffix[level]}";
    }

    /// <summary>
    ///     根据程序名统计流量
    /// </summary>
    public static void NetTraffic(Func<bool> shouldContinue, Action<bool> visibilityChanged, Action<ulong> updated)
    {
        NetTraffic(shouldContinue, visibilityChanged, (download, _) => updated(download));
    }

    public static void NetTraffic(Func<bool> shouldContinue, Action<bool> visibilityChanged, Action<ulong, ulong> updated)
    {
        if (!Flags.IsWindows10Upper)
            return;

        var counterLock = new object();
        //int sent = 0;

        var processes = new List<Process>();
        switch (MainController.ServerController)
        {
            case null:
                break;
            case Guard guard:
                processes.Add(guard.Instance);
                break;
        }

        if (!processes.Any())
            switch (MainController.ModeController)
            {
                case null:
                    break;
                case NFController or TUNController:
                    processes.Add(Process.GetCurrentProcess());
                    break;
                case Guard guard:
                    processes.Add(guard.Instance);
                    break;
            }

        var pidHastSet = processes.Select(instance => instance.Id).ToHashSet();

        Log.Information("Net traffic processes: {Processes}", string.Join(',', processes.Select(v => $"({v.Id}){v.ProcessName}")));

        received = 0;
        sent = 0;

        if (!processes.Any())
            return;

        visibilityChanged(true);

        Task.Run(() =>
            {
                tSession = new TraceEventSession("KernelAndClrEventsSession");
                tSession.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

                //这玩意儿上传和下载得到的data是一样的:)
                //所以暂时没办法区分上传下载流量
                tSession.Source.Kernel.TcpIpRecv += data =>
                {
                    if (pidHastSet.Contains(data.ProcessID))
                        lock (counterLock)
                            received += (ulong)data.size;

                    // Debug.WriteLine($"TcpIpRecv: {ToByteSize(data.size)}");
                };

                tSession.Source.Kernel.UdpIpRecv += data =>
                {
                    if (pidHastSet.Contains(data.ProcessID))
                        lock (counterLock)
                            received += (ulong)data.size;

                    // Debug.WriteLine($"UdpIpRecv: {ToByteSize(data.size)}");
                };

                tSession.Source.Kernel.TcpIpSend += data =>
                {
                    if (pidHastSet.Contains(data.ProcessID))
                        lock (counterLock)
                            sent += (ulong)data.size;
                };

                tSession.Source.Kernel.UdpIpSend += data =>
                {
                    if (pidHastSet.Contains(data.ProcessID))
                        lock (counterLock)
                            sent += (ulong)data.size;
                };

                tSession.Source.Process();
            })
            .Forget();

        while (shouldContinue())
        {
            Thread.Sleep(1000);
            lock (counterLock)
                updated(received, sent);
        }
    }

    public static void Stop()
    {
        tSession?.Dispose();
        received = 0;
        sent = 0;
    }
}

using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Netch.Interfaces;
using Netch.Models;
using Netch.Models.Modes;
using Netch.Models.Modes.TunMode;
using Netch.Servers;
using Netch.Utils;

namespace Netch.Controllers;

// Run the maintained Wintun client out of process: a native fault must not terminate the UI.
public class TUNController : IModeController
{
    private readonly DNSController _dns = new();
    private readonly List<NetRoute> _addedRoutes = new();
    private readonly SemaphoreSlim _logLock = new(1, 1);
    private Process? _process;
    private StreamWriter? _log;
    private Task _output = Task.CompletedTask;
    private Task _errors = Task.CompletedTask;
    private bool _dnsStarted;

    public string Name => "tun2socks";
    public string interfaceName => "netch";
    public ModeFeature Features => ModeFeature.SupportSocks5Auth;
    public Task ExitTask { get; private set; } = Task.CompletedTask;

    public async Task StartAsync(Socks5Server server, Mode mode)
    {
        if (mode is not TunMode tunMode)
            throw new InvalidOperationException();

        var executable = Path.Combine(Global.NetchDir, "bin", "tun2socks.exe");
        if (!File.Exists(executable) || !File.Exists(Path.Combine(Global.NetchDir, Constants.WintunDllFile)))
            throw new MessageException("TUN components are missing. Reinstall the complete Netch package.");
        var config = Global.Settings.TUNTAP;
        var outbound = NetRoute.GetBestRouteTemplate();
        var remote = await DnsUtils.LookupAsync(server.RemoteHostname.ValueOrDefault() ?? server.Hostname);
        var chinaDns = config.UseCustomDNS ? null : await DnsUtils.LookupAsync(Utils.Utils.GetHostFromUri(Global.Settings.AioDNS.ChinaDNS));
        var otherDns = config.UseCustomDNS ? null : await DnsUtils.LookupAsync(Utils.Utils.GetHostFromUri(Global.Settings.AioDNS.OtherDNS));
        var proxy = new UriBuilder("socks5", await server.AutoResolveHostnameAsync(), server.Port);
        if (server.Auth())
        {
            proxy.UserName = Uri.EscapeDataString(server.Username!);
            proxy.Password = Uri.EscapeDataString(server.Password!);
        }

        try
        {
            var logPath = Path.Combine(Global.NetchDir, "logging", "tun2socks.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            _log = new StreamWriter(new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
            var info = new ProcessStartInfo(executable)
            {
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in new[] { "--device", "tun://" + interfaceName, "--proxy", proxy.Uri.AbsoluteUri, "--mtu", "1500", "--loglevel", "info" })
                info.ArgumentList.Add(argument);
            _process = Process.Start(info) ?? throw new MessageException("TUN process could not be started.");
            Global.Job.AddProcess(_process);
            ExitTask = _process.WaitForExitAsync();
            _output = CopyLogAsync(_process.StandardOutput);
            _errors = CopyLogAsync(_process.StandardError);

            NetworkInterface? adapter = null;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                ThrowIfExited();
                adapter = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Name.Equals(interfaceName, StringComparison.OrdinalIgnoreCase));
                if (adapter != null) break;
                await Task.Delay(200);
            }
            if (adapter == null)
                throw new MessageException("TUN adapter creation timed out. See logging/tun2socks.log.");
            var index = adapter.GetIndex();
            await NetshAsync("interface", "ipv4", "set", "address", $"name={index}", "source=static", $"address={config.Address}", $"mask={config.Netmask}", "gateway=none", "store=active");
            await NetshAsync("interface", "ipv4", "set", "interface", $"interface={index}", "metric=1", "store=active");

            if (!config.UseCustomDNS)
            {
                await _dns.StartAsync();
                _dnsStarted = true;
            }
            await NetshAsync("interface", "ipv4", "set", "dnsservers", $"name={index}", "source=static", $"address={(config.UseCustomDNS ? config.DNS : "127.0.0.1")}", "validate=no");

            var tun = NetRoute.TemplateBuilder(config.Gateway, index);
            if (remote is { AddressFamily: AddressFamily.InterNetwork } && !IPAddress.IsLoopback(remote))
                AddRoute(outbound, remote + "/32", false);
            foreach (var rule in config.BypassIPs.Concat(tunMode.Bypass)) AddRoute(outbound, rule, false);
            if (config.UseCustomDNS)
                AddRoute(config.ProxyDNS ? tun : outbound, config.DNS + "/32", config.ProxyDNS);
            else
            {
                if (chinaDns is { AddressFamily: AddressFamily.InterNetwork }) AddRoute(outbound, chinaDns + "/32", false);
                if (otherDns is { AddressFamily: AddressFamily.InterNetwork }) AddRoute(tun, otherDns + "/32", true);
            }
            foreach (var rule in tunMode.Handle) AddRoute(tun, rule, true);
            ThrowIfExited();
            NativeMethods.RefreshDNSCache();
            Log.Information("TUN adapter ready: {Interface}, index {Index}, routes {Count}", interfaceName, index, _addedRoutes.Count);
        }
        catch
        {
            await StopAsync();
            throw;
        }
    }

    private void ThrowIfExited()
    {
        if (_process!.HasExited)
            throw new MessageException($"TUN process exited ({_process.ExitCode}). See logging/tun2socks.log.");
    }

    private void AddRoute(NetRoute template, string rule, bool required)
    {
        if (!RouteUtils.TryParseIPNetwork(rule, out var network, out var cidr) || cidr is < 0 or > 32 ||
            !IPAddress.TryParse(network, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            throw new MessageException($"Invalid IPv4 TUN route: {rule}");
        var route = template.FillTemplate(network, (byte)cidr);
        if (_addedRoutes.Contains(route)) return;
        if (RouteUtils.CreateRoute(route)) _addedRoutes.Add(route);
        else if (required) throw new MessageException($"Could not create TUN route: {rule}");
        else Log.Warning("Bypass route already exists or could not be created: {Route}", rule);
    }

    public async Task StopAsync()
    {
        // Only remove routes created by this instance; leave the physical NIC and its metric intact.
        for (var i = _addedRoutes.Count - 1; i >= 0; i--)
            if (!RouteUtils.DeleteRoute(_addedRoutes[i])) Log.Warning("Could not delete TUN route: {Route}", _addedRoutes[i].Network);
        _addedRoutes.Clear();
        if (_dnsStarted)
        {
            _dnsStarted = false;
            try { await _dns.StopAsync(); }
            catch (Exception exception) { Log.Warning(exception, "TUN DNS cleanup failed"); }
        }
        var process = _process;
        _process = null;
        if (process != null)
        {
            try
            {
                if (!process.HasExited) process.Kill();
                await process.WaitForExitAsync();
                // Both stream pumps run without a UI synchronization context.
#pragma warning disable VSTHRD003
                await Task.WhenAll(_output, _errors);
#pragma warning restore VSTHRD003
            }
            finally { process.Dispose(); }
        }
        if (_log != null)
        {
            await _log.DisposeAsync();
            _log = null;
        }
    }

    private async Task CopyLogAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            await _logLock.WaitAsync().ConfigureAwait(false);
            try { await _log!.WriteLineAsync(line).ConfigureAwait(false); }
            finally { _logLock.Release(); }
        }
    }

    private static async Task NetshAsync(params string[] arguments)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new MessageException("Could not configure the TUN adapter.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill();
            await process.WaitForExitAsync();
            throw new MessageException("TUN adapter configuration timed out.");
        }
        await Task.WhenAll(output, error);
        if (process.ExitCode != 0)
        {
            Log.Error("TUN adapter configuration failed: {Output} {Error}", await output, await error);
            throw new MessageException("Could not configure the TUN adapter. Run Netch as administrator and review the log.");
        }
    }

    public static void InstallDriver()
    {
        var source = Path.Combine(Global.NetchDir, Constants.WintunDllFile);
        if (!File.Exists(source)) throw new MessageException("The bundled Wintun library is missing.");
        File.Copy(source, Path.Combine(Environment.SystemDirectory, "wintun.dll"), true);
    }
}

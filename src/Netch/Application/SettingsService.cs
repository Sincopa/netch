using System.Net;
using System.Diagnostics.CodeAnalysis;
using Netch.Utils;

namespace Netch.Application;

public sealed class SettingsService
{
    private readonly ConnectionService _connection;
    private readonly SemaphoreSlim _updateGate = new(1, 1);

    public SettingsService(ConnectionService connection)
    {
        _connection = connection;
    }

    public SettingsDocumentDto GetDocument()
    {
        return new SettingsDocumentDto(GetSettings(), i18N.GetTranslateList(), _connection.CanChangeSettings);
    }

    public async Task<SettingsDocumentDto> UpdateAsync(SettingsDto settings, CancellationToken cancellationToken = default)
    {
        Validate(settings);
        await _updateGate.WaitAsync(cancellationToken);
        try
        {
            var previous = GetSettings();
            if (!_connection.CanChangeSettings && NetworkSettingsChanged(previous, settings))
                throw new AppException("SETTINGS_LOCKED", "Disconnect before changing connection, routing, DNS, or advanced settings.");

            var startupChanged = previous.Startup.RunAtStartup != settings.Startup.RunAtStartup;
            Apply(settings);
            try
            {
                if (startupChanged)
                    Utils.Utils.RegisterNetchStartupItem();
                DelayTestHelper.UpdateTick();
                await Configuration.SaveAsync();
            }
            catch (Exception exception)
            {
                Apply(previous);
                DelayTestHelper.UpdateTick();
                if (startupChanged)
                {
                    try
                    {
                        Utils.Utils.RegisterNetchStartupItem();
                    }
                    catch (Exception rollbackException)
                    {
                        Log.Warning(rollbackException, "Failed to restore Netch startup task");
                    }
                }

                throw new AppException("SETTINGS_SAVE_FAILED", "Settings could not be saved.", innerException: exception);
            }

            var document = GetDocument();
            AppEvents.Publish("settings.changed", document);
            return document;
        }
        finally
        {
            _updateGate.Release();
        }
    }

    public Task<SettingsDocumentDto> SetCloseToTrayAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var current = GetSettings();
        return UpdateAsync(current with
        {
            Startup = current.Startup with { CloseToTray = enabled }
        }, cancellationToken);
    }

    public static void Validate(SettingsDto settings)
    {
        if (settings is null || settings.General is null || settings.Connection is null || settings.Routing is null ||
            settings.Subscriptions is null || settings.Dns is null || settings.Startup is null || settings.Updates is null ||
            settings.Advanced is null || settings.Dns.BypassIps is null)
            Invalid("The settings document is incomplete.");

        if (string.IsNullOrWhiteSpace(settings.General.Language) || settings.General.Language.Length > 64)
            Invalid("Select a valid interface language.");
        if (!i18N.GetTranslateList().Contains(settings.General.Language, StringComparer.OrdinalIgnoreCase))
            Invalid("The selected interface language is not installed.");
        if (settings.General.ProfileCount is < 0 or > 100)
            Invalid("Profile count must be between 0 and 100.");
        if (settings.General.ProfileColumns is < 1 or > 20)
            Invalid("Profile columns must be between 1 and 20.");

        ValidatePort(settings.Connection.Socks5Port, "SOCKS5 port");
        ValidatePort(settings.Connection.HttpPort, "HTTP port");
        if (settings.Connection.Socks5Port == settings.Connection.HttpPort)
            Invalid("SOCKS5 and HTTP ports must be different.");
        if (!IPAddress.TryParse(settings.Connection.LocalAddress, out _))
            Invalid("Local proxy address must be a valid IP address.");
        if (settings.Connection.PingMethod is not ("tcp" or "icmp" or "http"))
            Invalid("Latency method must be TCP or ICMP.");
        if (settings.Connection.RequestTimeoutMs is < 1000 or > 120000)
            Invalid("Request timeout must be between 1000 and 120000 ms.");
        if (settings.Connection.DetectionIntervalSeconds is < 0 or > 86400)
            Invalid("Latency interval must be between 0 and 86400 seconds.");
        if (settings.Connection.StartupPingDelaySeconds is < -1 or > 86400)
            Invalid("Startup latency delay must be -1 or between 0 and 86400 seconds.");
        ValidateHost(settings.Connection.StunHost, "STUN host");
        ValidatePort(settings.Connection.StunPort, "STUN port");
        if (!Uri.TryCreate(settings.Connection.LatencyTestUrl, UriKind.Absolute, out var testUrl) ||
            testUrl.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(testUrl.UserInfo))
            Invalid("Latency test URL must be an HTTP or HTTPS address without credentials.");

        if (settings.Routing.IcmpDelayMs is < 0 or > 60000)
            Invalid("ICMP delay must be between 0 and 60000 ms.");
        ValidateEndpoint(settings.Routing.DnsHost, "Process DNS endpoint");

        ValidateIp(settings.Dns.TunAddress, "TUN address");
        ValidateIp(settings.Dns.TunNetmask, "TUN netmask");
        ValidateIp(settings.Dns.TunGateway, "TUN gateway");
        ValidateIp(settings.Dns.TunDns, "TUN DNS address");
        ValidateDnsUri(settings.Dns.ChinaDns, "China DNS endpoint");
        ValidateDnsUri(settings.Dns.OtherDns, "Other DNS endpoint");
        foreach (var bypass in settings.Dns.BypassIps)
            ValidateCidr(bypass);

        if (settings.Advanced.KcpMtu is < 576 or > 9000)
            Invalid("KCP MTU must be between 576 and 9000.");
        if (settings.Advanced.KcpTti is < 1 or > 10000)
            Invalid("KCP TTI must be between 1 and 10000.");
        ValidateNonNegative(settings.Advanced.KcpUplinkCapacity, "KCP uplink capacity");
        ValidateNonNegative(settings.Advanced.KcpDownlinkCapacity, "KCP downlink capacity");
        ValidateNonNegative(settings.Advanced.KcpReadBufferSize, "KCP read buffer size");
        ValidateNonNegative(settings.Advanced.KcpWriteBufferSize, "KCP write buffer size");
    }

    private static SettingsDto GetSettings()
    {
        var value = Global.Settings;
        return new SettingsDto(
            new GeneralSettingsDto(value.Language, value.ProfileCount, value.ProfileTableColumnCount),
            new ConnectionSettingsDto(
                value.Socks5LocalPort,
                value.HTTPLocalPort,
                value.LocalAddress,
                value.LatencyTestMethod,
                value.RequestTimeout,
                value.DetectionTick,
                value.StartedPingInterval,
                value.STUN_Server,
                value.STUN_Server_Port,
                value.LatencyTestUrl),
            new RoutingSettingsDto(
                value.Redirector.FilterTCP,
                value.Redirector.FilterUDP,
                value.Redirector.FilterICMP,
                value.Redirector.ICMPDelay,
                value.Redirector.FilterDNS,
                value.Redirector.FilterParent,
                value.Redirector.DNSProxy,
                value.Redirector.HandleOnlyDNS,
                value.Redirector.DNSHost),
            new SubscriptionSettingsDto(value.UpdateServersWhenOpened),
            new DnsSettingsDto(
                value.AioDNS.ChinaDNS,
                value.AioDNS.OtherDNS,
                value.TUNTAP.Address,
                value.TUNTAP.Netmask,
                value.TUNTAP.Gateway,
                value.TUNTAP.UseCustomDNS,
                value.TUNTAP.DNS,
                value.TUNTAP.ProxyDNS,
                value.TUNTAP.BypassIPs.ToArray()),
            new StartupSettingsDto(
                value.RunAtStartup,
                value.StartWhenOpened,
                value.MinimizeWhenStarted,
                !value.ExitWhenClosed,
                value.StopWhenExited),
            new UpdateSettingsDto(value.CheckUpdateWhenOpened, value.CheckBetaUpdate),
            new AdvancedSettingsDto(
                value.V2RayConfig.XrayCone,
                value.V2RayConfig.AllowInsecure,
                value.V2RayConfig.UseMux,
                value.V2RayConfig.TCPFastOpen,
                value.NoSupportDialog,
                value.V2RayConfig.KcpConfig.mtu,
                value.V2RayConfig.KcpConfig.tti,
                value.V2RayConfig.KcpConfig.uplinkCapacity,
                value.V2RayConfig.KcpConfig.downlinkCapacity,
                value.V2RayConfig.KcpConfig.readBufferSize,
                value.V2RayConfig.KcpConfig.writeBufferSize,
                value.V2RayConfig.KcpConfig.congestion));
    }

    private static void Apply(SettingsDto settings)
    {
        var value = Global.Settings;
        value.Language = settings.General.Language;
        value.ProfileCount = settings.General.ProfileCount;
        value.ProfileTableColumnCount = settings.General.ProfileColumns;

        value.Socks5LocalPort = settings.Connection.Socks5Port;
        value.HTTPLocalPort = settings.Connection.HttpPort;
        value.LocalAddress = settings.Connection.LocalAddress;
        value.ServerTCPing = settings.Connection.PingMethod == "tcp";
        value.LatencyTestMethod = settings.Connection.PingMethod;
        value.LatencyTestUrl = settings.Connection.LatencyTestUrl.Trim();
        value.RequestTimeout = settings.Connection.RequestTimeoutMs;
        value.DetectionTick = settings.Connection.DetectionIntervalSeconds;
        value.StartedPingInterval = settings.Connection.StartupPingDelaySeconds;
        value.STUN_Server = settings.Connection.StunHost;
        value.STUN_Server_Port = settings.Connection.StunPort;

        value.Redirector.FilterTCP = settings.Routing.FilterTcp;
        value.Redirector.FilterUDP = settings.Routing.FilterUdp;
        value.Redirector.FilterICMP = settings.Routing.FilterIcmp;
        value.Redirector.ICMPDelay = settings.Routing.IcmpDelayMs;
        value.Redirector.FilterDNS = settings.Routing.FilterDns;
        value.Redirector.FilterParent = settings.Routing.IncludeChildProcesses;
        value.Redirector.DNSProxy = settings.Routing.ProxyDns;
        value.Redirector.HandleOnlyDNS = settings.Routing.HandleOnlyDns;
        value.Redirector.DNSHost = settings.Routing.DnsHost;

        value.UpdateServersWhenOpened = settings.Subscriptions.UpdateOnLaunch;

        value.AioDNS.ChinaDNS = settings.Dns.ChinaDns;
        value.AioDNS.OtherDNS = settings.Dns.OtherDns;
        value.TUNTAP.Address = settings.Dns.TunAddress;
        value.TUNTAP.Netmask = settings.Dns.TunNetmask;
        value.TUNTAP.Gateway = settings.Dns.TunGateway;
        value.TUNTAP.UseCustomDNS = settings.Dns.UseCustomDns;
        value.TUNTAP.DNS = settings.Dns.TunDns;
        value.TUNTAP.ProxyDNS = settings.Dns.ProxyTunDns;
        value.TUNTAP.BypassIPs = settings.Dns.BypassIps.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        value.RunAtStartup = settings.Startup.RunAtStartup;
        value.StartWhenOpened = settings.Startup.ConnectOnLaunch;
        value.MinimizeWhenStarted = settings.Startup.MinimizeOnLaunch;
        value.ExitWhenClosed = !settings.Startup.CloseToTray;
        value.StopWhenExited = settings.Startup.StopConnectionOnExit;

        value.CheckUpdateWhenOpened = settings.Updates.CheckOnLaunch;
        value.CheckBetaUpdate = settings.Updates.IncludeBetaVersions;

        value.V2RayConfig.XrayCone = settings.Advanced.XrayCone;
        value.V2RayConfig.AllowInsecure = settings.Advanced.AllowInsecureTls;
        value.V2RayConfig.UseMux = settings.Advanced.UseMux;
        value.V2RayConfig.TCPFastOpen = settings.Advanced.TcpFastOpen;
        value.NoSupportDialog = settings.Advanced.HideUnsupportedEnvironmentWarning;
        value.V2RayConfig.KcpConfig.mtu = settings.Advanced.KcpMtu;
        value.V2RayConfig.KcpConfig.tti = settings.Advanced.KcpTti;
        value.V2RayConfig.KcpConfig.uplinkCapacity = settings.Advanced.KcpUplinkCapacity;
        value.V2RayConfig.KcpConfig.downlinkCapacity = settings.Advanced.KcpDownlinkCapacity;
        value.V2RayConfig.KcpConfig.readBufferSize = settings.Advanced.KcpReadBufferSize;
        value.V2RayConfig.KcpConfig.writeBufferSize = settings.Advanced.KcpWriteBufferSize;
        value.V2RayConfig.KcpConfig.congestion = settings.Advanced.KcpCongestion;
    }

    private static bool NetworkSettingsChanged(SettingsDto current, SettingsDto next)
    {
        return current.Connection != next.Connection || current.Routing != next.Routing || current.Advanced != next.Advanced ||
               current.Dns.ChinaDns != next.Dns.ChinaDns || current.Dns.OtherDns != next.Dns.OtherDns ||
               current.Dns.TunAddress != next.Dns.TunAddress || current.Dns.TunNetmask != next.Dns.TunNetmask ||
               current.Dns.TunGateway != next.Dns.TunGateway || current.Dns.UseCustomDns != next.Dns.UseCustomDns ||
               current.Dns.TunDns != next.Dns.TunDns || current.Dns.ProxyTunDns != next.Dns.ProxyTunDns ||
               !current.Dns.BypassIps.SequenceEqual(next.Dns.BypassIps, StringComparer.OrdinalIgnoreCase);
    }

    private static void ValidatePort(int port, string name)
    {
        if (port is < 1 or > 65535)
            Invalid($"{name} must be between 1 and 65535.");
    }

    private static void ValidateIp(string value, string name)
    {
        if (!IPAddress.TryParse(value, out _))
            Invalid($"{name} must be a valid IP address.");
    }

    private static void ValidateHost(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 253 || Uri.CheckHostName(value) == UriHostNameType.Unknown)
            Invalid($"{name} is invalid.");
    }

    private static void ValidateEndpoint(string value, string name)
    {
        if (!Uri.TryCreate($"udp://{value}", UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host) || uri.Port is < 1 or > 65535)
            Invalid($"{name} must use host:port format.");
    }

    private static void ValidateDnsUri(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.Any(char.IsWhiteSpace))
            Invalid($"{name} must use host:port, tcp://host:port, or udp://host:port format.");

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "tcp" or "udp" &&
            !string.IsNullOrWhiteSpace(uri.Host) && uri.Port is >= 1 and <= 65535)
            return;

        if (Uri.TryCreate($"udp://{value}", UriKind.Absolute, out uri) && !string.IsNullOrWhiteSpace(uri.Host) &&
            uri.Port is >= 1 and <= 65535)
            return;

        Invalid($"{name} must use host:port, tcp://host:port, or udp://host:port format.");
    }

    private static void ValidateCidr(string value)
    {
        var pieces = value.Split('/', StringSplitOptions.TrimEntries);
        if (pieces.Length != 2 || !IPAddress.TryParse(pieces[0], out var address) || !int.TryParse(pieces[1], out var prefix) ||
            prefix < 0 || prefix > (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128))
            Invalid($"Bypass entry '{value}' must be a valid IP/CIDR value.");
    }

    private static void ValidateNonNegative(int value, string name)
    {
        if (value is < 0 or > 65535)
            Invalid($"{name} must be between 0 and 65535.");
    }

    [DoesNotReturn]
    private static void Invalid(string message) => throw new AppException("INVALID_SETTINGS", message);
}

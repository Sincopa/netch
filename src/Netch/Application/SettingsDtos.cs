namespace Netch.Application;

public sealed record SettingsDocumentDto(
    SettingsDto Settings,
    IReadOnlyList<string> Languages,
    bool CanUpdate);

public sealed record SettingsDto(
    GeneralSettingsDto General,
    ConnectionSettingsDto Connection,
    RoutingSettingsDto Routing,
    SubscriptionSettingsDto Subscriptions,
    DnsSettingsDto Dns,
    StartupSettingsDto Startup,
    UpdateSettingsDto Updates,
    AdvancedSettingsDto Advanced);

public sealed record GeneralSettingsDto(
    string Language,
    int ProfileCount,
    byte ProfileColumns);

public sealed record ConnectionSettingsDto(
    ushort Socks5Port,
    ushort HttpPort,
    string LocalAddress,
    string PingMethod,
    int RequestTimeoutMs,
    int DetectionIntervalSeconds,
    int StartupPingDelaySeconds,
    string StunHost,
    int StunPort,
    string LatencyTestUrl = "https://www.google.com/generate_204");

public sealed record RoutingSettingsDto(
    bool FilterTcp,
    bool FilterUdp,
    bool FilterIcmp,
    int IcmpDelayMs,
    bool FilterDns,
    bool IncludeChildProcesses,
    bool ProxyDns,
    bool HandleOnlyDns,
    string DnsHost);

public sealed record SubscriptionSettingsDto(bool UpdateOnLaunch);

public sealed record DnsSettingsDto(
    string ChinaDns,
    string OtherDns,
    string TunAddress,
    string TunNetmask,
    string TunGateway,
    bool UseCustomDns,
    string TunDns,
    bool ProxyTunDns,
    IReadOnlyList<string> BypassIps);

public sealed record StartupSettingsDto(
    bool RunAtStartup,
    bool ConnectOnLaunch,
    bool MinimizeOnLaunch,
    bool CloseToTray,
    bool StopConnectionOnExit);

public sealed record UpdateSettingsDto(
    bool CheckOnLaunch,
    bool IncludeBetaVersions);

public sealed record AdvancedSettingsDto(
    bool XrayCone,
    bool AllowInsecureTls,
    bool UseMux,
    bool TcpFastOpen,
    bool HideUnsupportedEnvironmentWarning,
    int KcpMtu,
    int KcpTti,
    int KcpUplinkCapacity,
    int KcpDownlinkCapacity,
    int KcpReadBufferSize,
    int KcpWriteBufferSize,
    bool KcpCongestion);

public sealed record SettingsUpdateRequest(SettingsDto Settings);

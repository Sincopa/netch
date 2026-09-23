namespace Netch.Application;

public sealed record ModeEditorDto(
    string Id,
    string Name,
    string Kind,
    string Source,
    bool Editable,
    string FileName,
    bool? FilterIcmp,
    bool? FilterTcp,
    bool? FilterUdp,
    bool? FilterDns,
    bool? IncludeChildProcesses,
    int? IcmpDelayMs,
    bool? ProxyDns,
    bool? HandleOnlyDns,
    string? DnsHost,
    bool FilterLoopback,
    bool FilterIntranet,
    IReadOnlyList<string> BypassRules,
    IReadOnlyList<string> HandleRules,
    string? ShareArgument);

public sealed record ModeWriteRequest(
    string? Id,
    string Name,
    string Kind,
    bool? FilterIcmp,
    bool? FilterTcp,
    bool? FilterUdp,
    bool? FilterDns,
    bool? IncludeChildProcesses,
    int? IcmpDelayMs,
    bool? ProxyDns,
    bool? HandleOnlyDns,
    string? DnsHost,
    bool FilterLoopback,
    bool FilterIntranet,
    IReadOnlyList<string>? BypassRules,
    IReadOnlyList<string>? HandleRules);

public sealed record ModeIdRequest(string Id);

public sealed record ModeCatalogDto(
    IReadOnlyList<ModeDto> Modes,
    IReadOnlyList<ModeEditorDto> Details,
    string? SelectedModeId);

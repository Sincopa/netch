namespace Netch.Application;

public sealed record DiagnosticComponentDto(
    string Id,
    string Name,
    string Status,
    string Summary,
    string? Version,
    bool CanRepair);

public sealed record DiagnosticsSnapshotDto(
    string AppVersion,
    string RuntimeVersion,
    string OperatingSystem,
    string Architecture,
    bool IsAdministrator,
    string? WebView2Version,
    DateTimeOffset CheckedAt,
    IReadOnlyList<DiagnosticComponentDto> Components);

public sealed record DriverActionRequest(string DriverId);

public sealed record UpdateStateDto(
    string Status,
    string CurrentVersion,
    string? LatestVersion,
    string? ReleaseNotes,
    string? ReleaseUrl,
    int Progress,
    string? Error,
    bool CanDownload,
    bool CanApply);

public sealed record UpdateCheckRequest(bool IncludePrerelease);

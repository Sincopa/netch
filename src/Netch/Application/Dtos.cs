using System.Text.Json.Serialization;
using System.Text.Json;

namespace Netch.Application;

public sealed record ServerDto(
    string Id,
    string Name,
    string Group,
    string Protocol,
    string Hostname,
    ushort Port,
    int? Latency,
    bool ManagedBySubscription,
    bool IsFavorite,
    string? CountryCode,
    bool IsAutomatic = false,
    string? LatencyMethod = null);

public sealed record ModeDto(
    string Id,
    string Name,
    string Kind,
    bool SupportsProcessSelection,
    string? SelectionRole = null);

public sealed record SubscriptionDto(
    string Id,
    string Remark,
    string Url,
    string UserAgent,
    bool Enabled,
    int ServerCount,
    string RefreshStatus,
    DateTimeOffset? LastUpdatedAt,
    string? LastError);

public sealed record ProcessDto(
    IReadOnlyList<int> ProcessIds,
    string Name,
    string Executable,
    string? Path,
    string? IconDataUrl);

public sealed record LogEntryDto(
    long Id,
    DateTimeOffset Timestamp,
    string Level,
    string Message);

public sealed record ConnectionStateDto(
    string Status,
    string Message,
    string? ServerId,
    string? ModeId,
    DateTimeOffset? ConnectedAt);

public sealed record TrafficSnapshotDto(
    ulong ReceivedBytes,
    ulong SentBytes,
    string Received,
    string Sent,
    long DurationSeconds,
    DateTimeOffset? ConnectedAt);

public sealed record SubscriptionRefreshSummaryDto(
    int ServerCount,
    int UpdatedSubscriptions,
    int FailedSubscriptions);

public sealed record RoutingDto(string Mode, IReadOnlyList<string> Processes);

public sealed record BootstrapDto(
    IReadOnlyList<ServerDto> Servers,
    IReadOnlyList<ModeDto> Modes,
    IReadOnlyList<ProfileDto> Profiles,
    IReadOnlyList<SubscriptionDto> Subscriptions,
    ConnectionStateDto Connection,
    RoutingDto Routing,
    IReadOnlyList<LogEntryDto> Logs,
    string? SelectedServerId,
    string? SelectedModeId,
    bool CloseToTray,
    string Language = "System");

public sealed record ConnectRequest(string ServerId, string ModeId);

public sealed record SelectRequest(string Id);

public sealed record SubscriptionWriteRequest(
    string? Id,
    string Remark,
    string Url,
    string? UserAgent,
    bool Enabled = true);

public sealed record SubscriptionIdRequest(string Id);

public sealed record RoutingWriteRequest(IReadOnlyList<string> Processes);

public sealed record PingRequest(IReadOnlyList<string>? ServerIds);

public sealed record RpcRequest(string Id, string Method, JsonElement Params);

public sealed record RpcError(string Code, string Message, string? Details = null);

public sealed record RpcResponse(
    string Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? Result = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RpcError? Error = null);

public sealed class AppException : Exception
{
    public AppException(string code, string message, string? details = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Details = details;
    }

    public string Code { get; }

    public string? Details { get; }
}

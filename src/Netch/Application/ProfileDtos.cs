namespace Netch.Application;

public sealed record ProfileDto(
    int Slot,
    string Status,
    string? Name,
    string? ServerName,
    string? ModeName,
    string? ServerId,
    string? ModeId);

public sealed record ProfileWriteRequest(
    int Slot,
    string Name,
    string ServerId,
    string ModeId);

public sealed record ProfileSlotRequest(int Slot);

public sealed record ProfileActivationDto(string ServerId, string ModeId);

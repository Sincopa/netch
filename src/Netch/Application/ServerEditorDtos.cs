namespace Netch.Application;

public sealed record ServerEditorDto(
    string Id,
    string Name,
    string Group,
    string Hostname,
    int Port,
    string Protocol,
    bool ManagedBySubscription,
    IReadOnlyDictionary<string, string?> Values,
    IReadOnlyDictionary<string, bool> SecretConfigured);

public sealed record ServerEditorDocumentDto(
    IReadOnlyList<ServerEditorDto> Servers,
    IReadOnlyList<string> Protocols,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Options);

public sealed record ServerDetailsWriteRequest(
    string? Id,
    string Protocol,
    string Name,
    string Group,
    string Hostname,
    int Port,
    IReadOnlyDictionary<string, string?>? Values,
    IReadOnlyDictionary<string, string?>? Secrets,
    IReadOnlyList<string>? ClearSecrets);

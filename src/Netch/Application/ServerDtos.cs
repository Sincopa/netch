namespace Netch.Application;

public sealed record ServerCatalogDto(
    IReadOnlyList<ServerDto> Servers,
    string? SelectedServerId,
    int ImportedCount = 0);

public sealed record ServerImportRequest(string Text, string? Group);

public sealed record ServerIdRequest(string Id);

public sealed record ServerFavoriteRequest(string Id, bool IsFavorite);

using Netch.Models;
using Netch.Models.Modes;
using Netch.Models.Modes.ProcessMode;
using Netch.Services;
using Netch.Utils;

namespace Netch.Application;

public sealed class CatalogService
{
    public void LoadModes()
    {
        ModeService.Instance.Load();
        if (Global.Settings.RoutingPreferencesVersion == 0)
        {
            // The new built-in profile changes sorted indices. Resolve the old index
            // against the previous catalog once, then persist paths from now on.
            var previous = Global.Modes.Where(m => Path.GetFileName(m.FullName) != RoutingPreferences.SystemModeFile).ToArray();
            var index = Global.Settings.ModeComboBoxSelectedIndex;
            if (index >= 0 && index < previous.Length)
            {
                Global.Settings.ModeComboBoxSelectedIndex = Global.Modes.IndexOf(previous[index]);
                RoutingPreferences.RememberSelection();
            }
            Global.Settings.RoutingPreferencesVersion = 1;
        }
        if (RoutingPreferences.SavedPath != null) RoutingPreferences.RestoreSelection();
    }

    public IReadOnlyList<ServerDto> GetServers()
    {
        Global.Settings.FavoriteServers ??= new List<string>();
        var favoriteKeys = Global.Settings.FavoriteServers.ToHashSet(StringComparer.Ordinal);
        var subscriptionGroups = Global.Settings.Subscription
            .Select(subscription => subscription.Remark)
            .ToHashSet(StringComparer.Ordinal);
        return Global.Settings.Server.Select((server, index) => (server, index))
            .Where(item => HasEndpoint(item.server))
            .Select(item => { var (server, index) = item; return new ServerDto(
            index.ToString(),
            string.IsNullOrWhiteSpace(server.Remark) ? $"{server.Hostname}:{server.Port}" : server.Remark,
            server.Group,
            server.Type,
            server.Hostname,
            server.Port,
            server.Delay >= 0 ? server.Delay : null,
            !string.Equals(server.Group, Constants.DefaultGroup, StringComparison.OrdinalIgnoreCase) &&
            subscriptionGroups.Contains(server.Group),
            favoriteKeys.Contains(ServerIdentity.GetFavoriteKey(server)),
            server.IsAutomatic ? null : ServerIdentity.ResolveCountryCode(server.Remark, server.Group, server.Hostname),
            server.IsAutomatic, server.LatencyMethod); }).ToArray();
    }

    public static bool HasEndpoint(Server? server) => server is not null &&
        !string.IsNullOrWhiteSpace(server.Hostname) && server.Port > 0;

    public IReadOnlyList<ModeDto> GetModes()
    {
        return Global.Modes.Select((mode, index) => new ModeDto(
            index.ToString(),
            mode.i18NRemark,
            mode.Type.ToString(),
            mode is Redirector,
            RoutingPreferences.Role(mode))).ToArray();
    }

    public Server ResolveServer(string id)
    {
        if (!int.TryParse(id, out var index) || index < 0 || index >= Global.Settings.Server.Count)
            throw new AppException("SERVER_NOT_FOUND", "The selected server no longer exists.");

        var server = Global.Settings.Server[index];
        if (!HasEndpoint(server))
            throw new AppException("INVALID_SERVER", "This server has no valid address. Refresh its subscription or edit the server.");
        return server;
    }

    public Mode ResolveMode(string id)
    {
        if (!int.TryParse(id, out var index) || index < 0 || index >= Global.Modes.Count)
            throw new AppException("MODE_NOT_FOUND", "The selected mode no longer exists.");

        return Global.Modes[index];
    }

    public string? GetSelectedServerId()
    {
        var index = Global.Settings.ServerComboBoxSelectedIndex;
        return index >= 0 && index < Global.Settings.Server.Count && HasEndpoint(Global.Settings.Server[index])
            ? index.ToString() : GetServers().FirstOrDefault()?.Id;
    }

    public string? GetSelectedModeId()
    {
        var index = Global.Settings.ModeComboBoxSelectedIndex;
        return index >= 0 && index < Global.Modes.Count ? index.ToString()
            : GetModes().FirstOrDefault(m => m.SelectionRole == "whole-computer")?.Id ?? GetModes().FirstOrDefault()?.Id;
    }

    public async Task SelectServerAsync(string id)
    {
        _ = ResolveServer(id);
        var previousServer = Global.Settings.ServerComboBoxSelectedIndex;
        var previousMode = Global.Settings.ModeComboBoxSelectedIndex;
        var previousPath = RoutingPreferences.SavedPath;
        RoutingPreferences.RememberSelection();
        Global.Settings.ServerComboBoxSelectedIndex = int.Parse(id);
        RoutingPreferences.RestoreSelection();
        try { await Configuration.SaveAsync(); }
        catch
        {
            Global.Settings.ServerComboBoxSelectedIndex = previousServer;
            Global.Settings.ModeComboBoxSelectedIndex = previousMode;
            RoutingPreferences.SavedPath = previousPath;
            throw;
        }
        AppEvents.Publish("selection.changed", new { serverId = id, modeId = GetSelectedModeId() });
        AppEvents.Publish("modes.changed", true);
    }

    public async Task SelectModeAsync(string id)
    {
        _ = ResolveMode(id);
        var previousMode = Global.Settings.ModeComboBoxSelectedIndex;
        var previousPath = RoutingPreferences.SavedPath;
        Global.Settings.ModeComboBoxSelectedIndex = int.Parse(id);
        RoutingPreferences.RememberSelection();
        try { await Configuration.SaveAsync(); }
        catch
        {
            Global.Settings.ModeComboBoxSelectedIndex = previousMode;
            RoutingPreferences.SavedPath = previousPath;
            throw;
        }
        AppEvents.Publish("selection.changed", new { serverId = GetSelectedServerId(), modeId = id });
    }

    public async Task<IReadOnlyList<ServerDto>> PingAsync(IReadOnlyList<string>? ids, CancellationToken cancellationToken)
    {
        var targets = ids is { Count: > 0 }
            ? ids.Select(ResolveServer).Distinct().ToArray()
            : Global.Settings.Server.Where(HasEndpoint).ToArray();

        using var concurrency = new SemaphoreSlim(4);
        await Task.WhenAll(targets.Select(async server =>
        {
            await concurrency.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var latency = await server.PingAsync(cancellationToken);
                var index = Global.Settings.Server.IndexOf(server);
                AppEvents.Publish("servers.latencyChanged", new
                {
                    serverId = index.ToString(),
                    latency = latency >= 0 ? latency : (int?)null,
                    latencyMethod = server.LatencyMethod,
                    timedOut = latency < 0
                });
            }
            finally
            {
                concurrency.Release();
            }
        }));

        return GetServers();
    }
}

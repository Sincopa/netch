using Netch.Models;
using Netch.Models.Modes;
using Netch.Utils;

namespace Netch.Application;

public sealed class ProfileService
{
    private readonly CatalogService _catalog;
    private readonly ConnectionService? _connection;
    private readonly SemaphoreSlim _updateGate = new(1, 1);

    public ProfileService(CatalogService catalog, ConnectionService? connection = null)
    {
        _catalog = catalog;
        _connection = connection;
    }

    public IReadOnlyList<ProfileDto> GetAll()
    {
        return Enumerable.Range(0, Math.Clamp(Global.Settings.ProfileCount, 0, 100))
            .Select(GetSlot)
            .ToArray();
    }

    public async Task<IReadOnlyList<ProfileDto>> SaveAsync(ProfileWriteRequest request, CancellationToken cancellationToken = default)
    {
        ValidateSlot(request.Slot, Global.Settings.ProfileCount);
        var name = NormalizeName(request.Name);
        var server = _catalog.ResolveServer(request.ServerId);
        var mode = _catalog.ResolveMode(request.ModeId);

        await _updateGate.WaitAsync(cancellationToken);
        try
        {
            var previous = Global.Settings.Profiles.ToList();
            Global.Settings.Profiles.RemoveAll(profile => profile.Index == request.Slot);
            Global.Settings.Profiles.Add(new Profile(server, mode, name, request.Slot));

            await SaveOrRestoreAsync(previous);
            var profiles = GetAll();
            AppEvents.Publish("profiles.changed", profiles);
            return profiles;
        }
        finally
        {
            _updateGate.Release();
        }
    }

    public async Task<IReadOnlyList<ProfileDto>> DeleteAsync(int slot, CancellationToken cancellationToken = default)
    {
        ValidateSlot(slot, Global.Settings.ProfileCount);

        await _updateGate.WaitAsync(cancellationToken);
        try
        {
            var previous = Global.Settings.Profiles.ToList();
            Global.Settings.Profiles.RemoveAll(profile => profile.Index == slot);

            await SaveOrRestoreAsync(previous);
            var profiles = GetAll();
            AppEvents.Publish("profiles.changed", profiles);
            return profiles;
        }
        finally
        {
            _updateGate.Release();
        }
    }

    public async Task<ProfileActivationDto> ActivateAsync(int slot, CancellationToken cancellationToken = default)
    {
        ValidateSlot(slot, Global.Settings.ProfileCount);
        var profile = Global.Settings.Profiles.SingleOrDefault(value => value.Index == slot)
            ?? throw new AppException("PROFILE_EMPTY", "This quick-profile slot is empty.");

        var serverId = ResolveServerId(profile);
        var modeId = ResolveModeId(profile);
        if (serverId is null || modeId is null)
            throw new AppException("PROFILE_TARGET_MISSING", "The server or mode saved in this profile is no longer available.");

        await _updateGate.WaitAsync(cancellationToken);
        try
        {
            await using var lease = _connection is null ? null : await _connection.AcquireInactiveLeaseAsync(cancellationToken);
            var previousServer = Global.Settings.ServerComboBoxSelectedIndex;
            var previousMode = Global.Settings.ModeComboBoxSelectedIndex;
            RoutingPreferences.RememberSelection();
            Global.Settings.ServerComboBoxSelectedIndex = int.Parse(serverId);
            Global.Settings.ModeComboBoxSelectedIndex = int.Parse(modeId);
            var previousPath = RoutingPreferences.SavedPath;
            RoutingPreferences.RememberSelection();
            try
            {
                await Configuration.SaveAsync();
            }
            catch (Exception exception)
            {
                RoutingPreferences.SavedPath = previousPath;
                Global.Settings.ServerComboBoxSelectedIndex = previousServer;
                Global.Settings.ModeComboBoxSelectedIndex = previousMode;
                throw new AppException("PROFILE_ACTIVATION_FAILED", "The profile selection could not be saved.", innerException: exception);
            }

            var result = new ProfileActivationDto(serverId, modeId);
            AppEvents.Publish("selection.changed", new { serverId, modeId });
            AppEvents.Publish("modes.changed", true);
            return result;
        }
        finally
        {
            _updateGate.Release();
        }
    }

    public static void ValidateSlot(int slot, int slotCount)
    {
        if (slotCount is < 0 or > 100 || slot < 0 || slot >= slotCount)
            throw new AppException("INVALID_PROFILE_SLOT", "The quick-profile slot is outside the configured range.");
    }

    public static string NormalizeName(string? name)
    {
        var value = name?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 || value.Any(char.IsControl))
            throw new AppException("INVALID_PROFILE_NAME", "Profile name must contain 1 to 64 visible characters.");
        return value;
    }

    private ProfileDto GetSlot(int slot)
    {
        var profile = Global.Settings.Profiles.SingleOrDefault(value => value.Index == slot);
        if (profile is null)
            return new ProfileDto(slot, "empty", null, null, null, null, null);

        var serverId = ResolveServerId(profile);
        var modeId = ResolveModeId(profile);
        return new ProfileDto(
            slot,
            serverId is not null && modeId is not null ? "ready" : "missing",
            string.IsNullOrWhiteSpace(profile.ProfileName) ? $"Profile {slot + 1}" : profile.ProfileName,
            profile.ServerRemark,
            profile.ModeRemark,
            serverId,
            modeId);
    }

    private static string? ResolveServerId(Profile profile)
    {
        var index = Global.Settings.Server.FindIndex(server =>
            string.Equals(server.Remark, profile.ServerRemark, StringComparison.Ordinal));
        return index >= 0 ? index.ToString() : null;
    }

    private static string? ResolveModeId(Profile profile)
    {
        var index = Global.Modes.FindIndex(mode => ModeMatches(mode, profile.ModeRemark));
        return index >= 0 ? index.ToString() : null;
    }

    private static bool ModeMatches(Mode mode, string remark)
    {
        return mode.Remark.Values.Any(value => string.Equals(value, remark, StringComparison.Ordinal));
    }

    private static async Task SaveOrRestoreAsync(IReadOnlyList<Profile> previous)
    {
        try
        {
            await Configuration.SaveAsync();
        }
        catch (Exception exception)
        {
            Global.Settings.Profiles.Clear();
            Global.Settings.Profiles.AddRange(previous);
            throw new AppException("PROFILE_SAVE_FAILED", "Quick profiles could not be saved.", innerException: exception);
        }
    }
}

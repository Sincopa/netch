using Netch.Models;
using Netch.Models.Modes;
using Netch.Models.Modes.ProcessMode;
using Netch.Models.Modes.TunMode;
using Netch.Services;

namespace Netch.Application;

public static class RoutingPreferences
{
    public const string SystemModeFile = "System VPN.json";
    public const string LegacyApplicationsFile = "WebUI Selected Applications.json";

    public static Subscription? CurrentSubscription()
    {
        var index = Global.Settings.ServerComboBoxSelectedIndex;
        if (index < 0 || index >= Global.Settings.Server.Count) return null;
        var group = Global.Settings.Server[index].Group;
        return group == Constants.DefaultGroup ? null : Global.Settings.Subscription.FirstOrDefault(s => s.Remark == group);
    }

    public static string ApplicationsFile()
    {
        var subscription = CurrentSubscription();
        if (subscription == null) return LegacyApplicationsFile;
        if (!Guid.TryParseExact(subscription.RoutingId, "N", out _))
            subscription.RoutingId = Guid.NewGuid().ToString("N");
        return $"WebUI Applications {subscription.RoutingId}.json";
    }

    public static string? SavedPath
    {
        get => CurrentSubscription() is { } subscription ? subscription.RoutingModePath : Global.Settings.LocalRoutingModePath;
        set
        {
            if (CurrentSubscription() is { } subscription) subscription.RoutingModePath = value;
            else Global.Settings.LocalRoutingModePath = value;
        }
    }

    public static string? Role(Mode mode) => mode is TunMode && string.Equals(mode.FullName,
        ModeService.Instance.GetFullPath(SystemModeFile), StringComparison.OrdinalIgnoreCase)
        ? "whole-computer"
        : mode is Redirector && Path.GetFileName(mode.FullName) == ApplicationsFile() ? "selected-applications" : null;

    public static void RememberSelection()
    {
        var index = Global.Settings.ModeComboBoxSelectedIndex;
        if (index >= 0 && index < Global.Modes.Count)
            SavedPath = ModeService.Instance.GetRelativePath(Global.Modes[index].FullName);
    }

    public static void RestoreSelection()
    {
        var path = SavedPath;
        var index = path == null ? -1 : Global.Modes.FindIndex(m => string.Equals(
            ModeService.Instance.GetRelativePath(m.FullName), path, StringComparison.OrdinalIgnoreCase));
        if (index < 0) index = Global.Modes.FindIndex(m => Role(m) == "whole-computer");
        if (index >= 0) Global.Settings.ModeComboBoxSelectedIndex = index;
    }

    public static Redirector? ApplicationsMode() => Global.Modes.OfType<Redirector>().FirstOrDefault(m =>
        Path.GetFileName(m.FullName) == ApplicationsFile()) ??
        // The legacy shared list seeds each subscription until its first scoped save.
        // Changing to Whole computer must not hide that list during migration.
        Global.Modes.OfType<Redirector>().FirstOrDefault(m => Path.GetFileName(m.FullName) == LegacyApplicationsFile);
}

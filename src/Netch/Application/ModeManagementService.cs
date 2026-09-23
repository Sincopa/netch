using Netch.Models.Modes;
using Netch.Models.Modes.ProcessMode;
using Netch.Models.Modes.ShareMode;
using Netch.Models.Modes.TunMode;
using Netch.Services;
using Netch.Utils;

namespace Netch.Application;

public sealed class ModeManagementService
{
    private const int MaximumRules = 2000;
    private const int MaximumRuleLength = 1024;
    private readonly CatalogService _catalog;
    private readonly ConnectionService _connection;
    private readonly SemaphoreSlim _updateGate = new(1, 1);

    public ModeManagementService(CatalogService catalog, ConnectionService connection)
    {
        _catalog = catalog;
        _connection = connection;
    }

    public ModeCatalogDto GetCatalog() => new(_catalog.GetModes(), GetAll(), _catalog.GetSelectedModeId());

    public IReadOnlyList<ModeEditorDto> GetAll()
    {
        return Global.Modes.Select((mode, index) => ToDto(mode, index)).ToArray();
    }

    public async Task<ModeCatalogDto> SaveAsync(ModeWriteRequest request, CancellationToken cancellationToken = default)
    {
        var name = NormalizeName(request.Name);
        var kind = NormalizeEditableKind(request.Kind);
        var bypass = NormalizeRules(request.BypassRules, "bypass");
        var handle = NormalizeRules(request.HandleRules, "handle");
        ValidateProcessOptions(kind, request);

        await _updateGate.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await _connection.AcquireInactiveLeaseAsync(cancellationToken);
            var previousModes = Global.Modes.ToList();
            var previousSelectedIndex = Global.Settings.ModeComboBoxSelectedIndex;
            var previousRoutingPath = RoutingPreferences.SavedPath;
            var previousProfiles = Global.Settings.Profiles.Select(profile => profile.ModeRemark).ToArray();
            Mode? previousMode = null;
            byte[]? previousFile = null;
            string target;

            if (request.Id is null)
            {
                target = GetNewCustomPath(name);
                if (File.Exists(target))
                    throw new AppException("MODE_FILE_EXISTS", "A custom mode with this file name already exists.");
            }
            else
            {
                previousMode = _catalog.ResolveMode(request.Id);
                EnsureEditable(previousMode);
                if (previousMode.Type != kind)
                    throw new AppException("MODE_KIND_LOCKED", "The type of an existing mode cannot be changed.");
                target = Path.GetFullPath(previousMode.FullName);
                previousFile = File.ReadAllBytes(target);
            }

            var replacement = CreateMode(kind, request, name, bypass, handle);
            if (previousMode is not null)
            {
                replacement.Remark = new Dictionary<string, string>(previousMode.Remark, StringComparer.OrdinalIgnoreCase);
                replacement.i18NRemark = name;
                if (!replacement.Remark.ContainsKey("en"))
                    replacement.Remark["en"] = name;
            }
            replacement.FullName = target;
            try
            {
                WriteAtomic(replacement);
                if (previousMode is null)
                    Global.Modes.Add(replacement);
                else
                {
                    var index = Global.Modes.IndexOf(previousMode);
                    Global.Modes[index] = replacement;
                    var previousName = previousMode.i18NRemark;
                    foreach (var profile in Global.Settings.Profiles.Where(profile => profile.ModeRemark == previousName))
                        profile.ModeRemark = name;
                }

                ModeService.Instance.Sort();
                Global.Settings.ModeComboBoxSelectedIndex = Global.Modes.IndexOf(replacement);
                RoutingPreferences.RememberSelection();
                await Configuration.SaveAsync();
            }
            catch (Exception exception)
            {
                RestoreState(previousModes, previousSelectedIndex, previousProfiles);
                RoutingPreferences.SavedPath = previousRoutingPath;
                RestoreFile(target, previousFile);
                if (exception is AppException)
                    throw;
                throw new AppException("MODE_SAVE_FAILED", "The mode could not be saved.", innerException: exception);
            }

            return PublishChanges();
        }
        finally
        {
            _updateGate.Release();
        }
    }

    public async Task<ModeCatalogDto> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _updateGate.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await _connection.AcquireInactiveLeaseAsync(cancellationToken);
            var mode = _catalog.ResolveMode(id);
            EnsureEditable(mode);
            var target = Path.GetFullPath(mode.FullName);
            var backup = target + ".delete-" + Guid.NewGuid().ToString("N") + ".tmp";
            var previousModes = Global.Modes.ToList();
            var previousSelectedIndex = Global.Settings.ModeComboBoxSelectedIndex;
            var previousRoutingPath = RoutingPreferences.SavedPath;

            try
            {
                File.Move(target, backup);
                var removedIndex = Global.Modes.IndexOf(mode);
                Global.Modes.Remove(mode);
                Global.Settings.ModeComboBoxSelectedIndex = Global.Modes.Count == 0
                    ? -1
                    : Math.Min(Math.Max(removedIndex, 0), Global.Modes.Count - 1);
                RoutingPreferences.RememberSelection();
                await Configuration.SaveAsync();
                File.Delete(backup);
            }
            catch (Exception exception)
            {
                Global.Modes.Clear();
                Global.Modes.AddRange(previousModes);
                Global.Settings.ModeComboBoxSelectedIndex = previousSelectedIndex;
                RoutingPreferences.SavedPath = previousRoutingPath;
                if (File.Exists(backup))
                    File.Move(backup, target, true);
                if (exception is AppException)
                    throw;
                throw new AppException("MODE_DELETE_FAILED", "The mode could not be deleted.", innerException: exception);
            }

            return PublishChanges();
        }
        finally
        {
            _updateGate.Release();
        }
    }

    public static string NormalizeName(string? value)
    {
        var name = value?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.Any(char.IsControl))
            throw new AppException("INVALID_MODE_NAME", "Mode name must contain 1 to 80 visible characters.");
        return name;
    }

    public static IReadOnlyList<string> NormalizeRules(IReadOnlyList<string>? values, string label)
    {
        if (values is null)
            return Array.Empty<string>();
        if (values.Count > MaximumRules)
            throw new AppException("INVALID_MODE_RULES", $"The {label} list cannot contain more than {MaximumRules} rules.");

        var result = new List<string>(values.Count);
        foreach (var raw in values)
        {
            if (raw is null)
                throw new AppException("INVALID_MODE_RULES", $"Each {label} rule must be a string.");
            var rule = raw.Trim();
            if (rule.Length == 0)
                continue;
            if (rule.Length > MaximumRuleLength || rule.Any(character => char.IsControl(character)))
                throw new AppException("INVALID_MODE_RULES", $"Each {label} rule must contain at most {MaximumRuleLength} visible characters.");
            result.Add(rule);
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private ModeCatalogDto PublishChanges()
    {
        var result = GetCatalog();
        AppEvents.Publish("modes.changed", result.Modes);
        AppEvents.Publish("selection.changed", new { serverId = _catalog.GetSelectedServerId(), modeId = result.SelectedModeId });
        return result;
    }

    private static ModeType NormalizeEditableKind(string value)
    {
        return value switch
        {
            "ProcessMode" => ModeType.ProcessMode,
            "TunMode" => ModeType.TunMode,
            _ => throw new AppException("INVALID_MODE_KIND", "Only Process and TUN modes can be created or edited.")
        };
    }

    private static void ValidateProcessOptions(ModeType kind, ModeWriteRequest request)
    {
        if (kind != ModeType.ProcessMode)
            return;
        if (request.IcmpDelayMs is < 0 or > 60000)
            throw new AppException("INVALID_MODE_OPTIONS", "ICMP delay must be between 0 and 60000 ms.");
        if (!string.IsNullOrWhiteSpace(request.DnsHost) &&
            (!Uri.TryCreate($"udp://{request.DnsHost}", UriKind.Absolute, out var endpoint) ||
             string.IsNullOrWhiteSpace(endpoint.Host) || endpoint.Port is < 1 or > 65535))
            throw new AppException("INVALID_MODE_OPTIONS", "DNS endpoint must use host:port format.");
    }

    private static Mode CreateMode(
        ModeType kind,
        ModeWriteRequest request,
        string name,
        IReadOnlyList<string> bypass,
        IReadOnlyList<string> handle)
    {
        Mode mode = kind switch
        {
            ModeType.ProcessMode => new Redirector
            {
                FilterICMP = request.FilterIcmp,
                FilterTCP = request.FilterTcp,
                FilterUDP = request.FilterUdp,
                FilterDNS = request.FilterDns,
                FilterParent = request.IncludeChildProcesses,
                ICMPDelay = request.IcmpDelayMs,
                DNSProxy = request.ProxyDns,
                HandleOnlyDNS = request.HandleOnlyDns,
                DNSHost = string.IsNullOrWhiteSpace(request.DnsHost) ? null : request.DnsHost.Trim(),
                FilterLoopback = request.FilterLoopback,
                FilterIntranet = request.FilterIntranet,
                Bypass = bypass.ToList(),
                Handle = handle.ToList()
            },
            ModeType.TunMode => new TunMode { Bypass = bypass.ToList(), Handle = handle.ToList() },
            _ => throw new InvalidOperationException()
        };
        mode.Remark[i18N.LangCode] = name;
        if (!string.Equals(i18N.LangCode, "en", StringComparison.OrdinalIgnoreCase))
            mode.Remark["en"] = name;
        return mode;
    }

    private static ModeEditorDto ToDto(Mode mode, int index)
    {
        var editable = IsCustomJson(mode);
        var source = editable ? "custom" : "built-in";
        var fileName = Path.GetRelativePath(ModeService.Instance.ModeDirectoryFullName, mode.FullName);
        return mode switch
        {
            Redirector process => new ModeEditorDto(
                index.ToString(), mode.i18NRemark, mode.Type.ToString(), source, editable, fileName,
                process.FilterICMP, process.FilterTCP, process.FilterUDP, process.FilterDNS, process.FilterParent,
                process.ICMPDelay, process.DNSProxy, process.HandleOnlyDNS, process.DNSHost,
                process.FilterLoopback, process.FilterIntranet, process.Bypass, process.Handle, null),
            TunMode tun => new ModeEditorDto(
                index.ToString(), mode.i18NRemark, mode.Type.ToString(), source, editable, fileName,
                null, null, null, null, null, null, null, null, null, false, false,
                tun.Bypass, tun.Handle, null),
            ShareMode share => new ModeEditorDto(
                index.ToString(), mode.i18NRemark, mode.Type.ToString(), source, false, fileName,
                null, null, null, null, null, null, null, null, null, false, false,
                Array.Empty<string>(), Array.Empty<string>(), share.Argument),
            _ => throw new InvalidOperationException("Unknown mode type.")
        };
    }

    private static void EnsureEditable(Mode mode)
    {
        if (mode is ShareMode || !IsCustomJson(mode))
            throw new AppException("MODE_READ_ONLY", "Built-in and Share modes are read-only. Create a custom Process or TUN mode instead.");
    }

    private static bool IsCustomJson(Mode mode)
    {
        if (!string.Equals(Path.GetExtension(mode.FullName), ".json", StringComparison.OrdinalIgnoreCase))
            return false;
        var root = GetCustomRoot() + Path.DirectorySeparatorChar;
        return Path.GetFullPath(mode.FullName).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetNewCustomPath(string name)
    {
        var safe = string.Concat(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();
        if (safe.Length == 0 || safe is "." or "..")
            throw new AppException("INVALID_MODE_NAME", "Mode name cannot be converted to a safe file name.");
        var root = GetCustomRoot();
        Directory.CreateDirectory(root);
        return Path.Combine(root, safe + ".json");
    }

    private static string GetCustomRoot() => Path.GetFullPath(Path.Combine(ModeService.Instance.ModeDirectoryFullName, "Custom"));

    private static void WriteAtomic(Mode mode)
    {
        var target = Path.GetFullPath(mode.FullName);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + ".write-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            mode.FullName = temporary;
            mode.WriteFile();
            mode.FullName = target;
            File.Move(temporary, target, true);
        }
        finally
        {
            mode.FullName = target;
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void RestoreFile(string target, byte[]? content)
    {
        try
        {
            if (content is null)
                File.Delete(target);
            else
            {
                var temporary = target + ".rollback-" + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(temporary, content);
                File.Move(temporary, target, true);
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Failed to restore mode file {Target}", target);
        }
    }

    private static void RestoreState(IReadOnlyList<Mode> modes, int selectedIndex, IReadOnlyList<string> profileModes)
    {
        Global.Modes.Clear();
        Global.Modes.AddRange(modes);
        Global.Settings.ModeComboBoxSelectedIndex = selectedIndex;
        for (var index = 0; index < Math.Min(profileModes.Count, Global.Settings.Profiles.Count); index++)
            Global.Settings.Profiles[index].ModeRemark = profileModes[index];
    }
}

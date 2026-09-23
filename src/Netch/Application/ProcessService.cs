using System.Diagnostics;
using System.Drawing.Imaging;
using Netch.Models.Modes.ProcessMode;
using Netch.Services;
using Netch.Utils;

namespace Netch.Application;

public sealed class ProcessService
{
    private readonly ConnectionService _connection;
    private readonly SemaphoreSlim _updateGate = new(1, 1);

    public ProcessService(ConnectionService connection)
    {
        _connection = connection;
    }

    public IReadOnlyList<ProcessDto> GetRunning(CancellationToken cancellationToken = default)
    {
        var candidates = new List<ProcessCandidate>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var executable = process.ProcessName + ".exe";
                string? path = null;
                try
                {
                    path = process.MainModule?.FileName;
                }
                catch
                {
                    // Protected system process.
                }

                candidates.Add(new ProcessCandidate(process.Id, executable, path));
            }
            catch
            {
                // The process exited while it was enumerated.
            }
            finally
            {
                process.Dispose();
            }
        }

        return candidates
            .GroupBy(process => process.Executable, StringComparer.OrdinalIgnoreCase)
            .Select(group => CreateDto(group.Key, group.Select(item => item.Id), group.Select(item => item.Path).FirstOrDefault(path => path is not null)))
            .OrderBy(process => process.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(process => process.Executable, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public ProcessDto GetExecutable(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new AppException("INVALID_EXECUTABLE", "No executable was selected.");

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new AppException("INVALID_EXECUTABLE", "The selected executable path is invalid.");
        }

        if (!File.Exists(fullPath) || !string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new AppException("INVALID_EXECUTABLE", "Select an existing Windows executable (.exe).");

        return CreateDto(Path.GetFileName(fullPath), Array.Empty<int>(), fullPath);
    }

    public RoutingDto GetRouting()
    {
        var mode = RoutingPreferences.ApplicationsMode();
        var processes = mode?.Handle.Select(NormalizeRuleForDisplay).Where(value => value is not null).Cast<string>().ToArray()
            ?? Array.Empty<string>();
        var index = Global.Settings.ModeComboBoxSelectedIndex;
        var selected = index >= 0 && index < Global.Modes.Count && Global.Modes[index] is Redirector;
        return new RoutingDto(selected ? "selected" : "all", processes);
    }

    public async Task<object> SaveSelectedAsync(IReadOnlyList<string> processes, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeProcesses(processes);
        if (normalized.Length == 0)
            throw new AppException("INVALID_PROCESS_SELECTION", "Select at least one application, or use Whole computer.");

        await _updateGate.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await _connection.AcquireInactiveLeaseAsync(cancellationToken);
            var customDirectory = Path.Combine(ModeService.Instance.ModeDirectoryFullName, "Custom");
            Directory.CreateDirectory(customDirectory);
            var fullName = Path.Combine(customDirectory, RoutingPreferences.ApplicationsFile());
            var previousPath = RoutingPreferences.SavedPath;
            var previousFile = File.Exists(fullName) ? File.ReadAllBytes(fullName) : null;
            var previousModes = Global.Modes.ToList();
            var previousSelection = Global.Settings.ModeComboBoxSelectedIndex;
            var mode = Global.Modes.OfType<Redirector>().FirstOrDefault(value =>
                string.Equals(value.FullName, fullName, StringComparison.OrdinalIgnoreCase));
            var previousHandle = mode?.Handle.ToList();

            if (mode is null)
            {
                mode = new Redirector
                {
                    FullName = fullName,
                    Remark = new Dictionary<string, string> { ["en"] = "Selected applications" }
                };
            }

            mode.Handle = normalized.Select(value => value.ToRegexString()).ToList();
            try
            {
                WriteModeAtomic(mode);
                ModeService.Instance.Load();
                var modeIndex = Global.Modes.FindIndex(value => string.Equals(value.FullName, fullName, StringComparison.OrdinalIgnoreCase));
                Global.Settings.ModeComboBoxSelectedIndex = modeIndex;
                RoutingPreferences.RememberSelection();
                await Configuration.SaveAsync();

                var routing = new RoutingDto(normalized.Length == 0 ? "all" : "selected", normalized);
                AppEvents.Publish("routing.changed", routing);
                AppEvents.Publish("modes.changed", true);
                return new { routing, modeId = modeIndex.ToString() };
            }
            catch (Exception exception)
            {
                Global.Modes.Clear();
                if (previousHandle is not null)
                    mode.Handle = previousHandle;
                Global.Modes.AddRange(previousModes);
                Global.Settings.ModeComboBoxSelectedIndex = previousSelection;
                RoutingPreferences.SavedPath = previousPath;
                if (previousFile is null)
                    File.Delete(fullName);
                else
                    File.WriteAllBytes(fullName, previousFile);
                throw new AppException("ROUTING_SAVE_FAILED", "Process routing could not be saved.", innerException: exception);
            }
        }
        finally
        {
            _updateGate.Release();
        }
    }

    public static string[] NormalizeProcesses(IReadOnlyList<string>? processes)
    {
        if (processes is null || processes.Count > 512)
            throw new AppException("INVALID_PROCESS_SELECTION", "Select at most 512 application entries.");
        if (processes.Any(value => value is null || value.Length > 1024 || value.Any(char.IsControl)))
            throw new AppException("INVALID_PROCESS_SELECTION", "Application entries must be valid executable names or paths.");

        var normalized = processes
            .Select(value => Path.GetFileName(value.Trim()))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? value : value + ".exe")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length > 128)
            throw new AppException("INVALID_PROCESS_SELECTION", "Select at most 128 unique applications.");
        return normalized;
    }

    private static void WriteModeAtomic(Redirector mode)
    {
        var target = mode.FullName;
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

    private static string? NormalizeRuleForDisplay(string rule)
    {
        if (string.IsNullOrWhiteSpace(rule) || rule.StartsWith('^'))
            return null;

        return rule.Replace("\\.", ".").Replace("\\", string.Empty);
    }

    private static ProcessDto CreateDto(string executable, IEnumerable<int> processIds, string? path)
    {
        var ids = processIds.Distinct().Order().ToArray();
        var name = Path.GetFileNameWithoutExtension(executable);
        if (path is not null)
        {
            try
            {
                var version = FileVersionInfo.GetVersionInfo(path);
                name = FirstNonEmpty(version.FileDescription, version.ProductName, name);
            }
            catch
            {
                // File metadata is optional.
            }
        }

        return new ProcessDto(ids, name, executable, path, ReadIcon(path));
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value))!;

    private static string? ReadIcon(string? path)
    {
        if (path is null)
            return null;

        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null)
                return null;

            using var source = icon.ToBitmap();
            using var bitmap = new Bitmap(source, new Size(24, 24));
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return $"data:image/png;base64,{Convert.ToBase64String(stream.ToArray())}";
        }
        catch
        {
            return null;
        }
    }

    private sealed record ProcessCandidate(int Id, string Executable, string? Path);
}

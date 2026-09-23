using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Web.WebView2.Core;
using Netch.Controllers;

namespace Netch.Application;

public sealed class DiagnosticsService
{
    public Task<DiagnosticsSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var components = new List<DiagnosticComponentDto>
        {
            GetWebView2Status(),
            GetFileStatus("xray", "Xray core", Path.Combine(Global.NetchDir, "bin", "xray.exe"), false),
            GetWintunStatus(),
            GetFileStatus("tun2socks", "TUN core", Path.Combine(Global.NetchDir, "bin", "tun2socks.exe"), false),
            GetNetfilterStatus(),
            GetFileStatus("geoip", "GeoIP database", Path.Combine(Global.NetchDir, "bin", "GeoLite2-Country.mmdb"), false),
            GetModeStatus()
        };

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new DiagnosticsSnapshotDto(
            UpdateChecker.Version,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            IsAdministrator(),
            GetWebView2Version(),
            DateTimeOffset.UtcNow,
            components));
    }

    private static DiagnosticComponentDto GetWebView2Status()
    {
        var version = GetWebView2Version();
        return new DiagnosticComponentDto(
            "webview2",
            "Microsoft WebView2 Runtime",
            version is null ? "missing" : "ready",
            version is null ? "Install the WebView2 Runtime to open the desktop interface." : "Desktop rendering runtime is available.",
            version,
            false);
    }

    private static DiagnosticComponentDto GetWintunStatus()
    {
        var bundled = Path.Combine(Global.NetchDir, Constants.WintunDllFile);
        var exists = File.Exists(bundled);
        return new DiagnosticComponentDto("wintun", "Wintun", exists ? "ready" : "missing-source",
            exists ? "TUN support is ready." : "The bundled Wintun library is missing.",
            exists ? Utils.Utils.GetFileVersion(bundled) : null, false);
    }

    private static DiagnosticComponentDto GetNetfilterStatus()
    {
        var builtIn = Path.Combine(Global.NetchDir, Constants.NFDriver);
        var installed = NFController.SystemDriverPath;
        if (!File.Exists(builtIn))
            return new DiagnosticComponentDto("netfilter2", "NetFilter2 driver", "missing-source", "The bundled process-routing driver is missing.", null, false);
        if (!File.Exists(installed))
            return new DiagnosticComponentDto("netfilter2", "NetFilter2 driver", "missing", "The process-routing driver is not installed.", null, true);

        var bundledVersion = Utils.Utils.GetFileVersion(builtIn);
        var installedVersion = Utils.Utils.GetFileVersion(installed);
        var current = string.Equals(bundledVersion, installedVersion, StringComparison.OrdinalIgnoreCase);
        return new DiagnosticComponentDto(
            "netfilter2",
            "NetFilter2 driver",
            current ? "ready" : "outdated",
            current ? "Selected-application routing is ready." : "The process-routing driver should be repaired.",
            installedVersion,
            true);
    }

    private static DiagnosticComponentDto GetModeStatus()
    {
        var count = Global.Modes.Count;
        return new DiagnosticComponentDto(
            "modes",
            "Connection modes",
            count > 0 ? "ready" : "missing",
            count > 0 ? $"{count} mode files loaded." : "No valid mode files were loaded.",
            count.ToString(),
            false);
    }

    private static DiagnosticComponentDto GetFileStatus(string id, string name, string path, bool repairable)
    {
        var exists = File.Exists(path);
        return new DiagnosticComponentDto(id, name, exists ? "ready" : "missing", exists ? "Component is available." : "Component file is missing.", null, repairable);
    }

    private static string? GetWebView2Version()
    {
        try
        {
            return CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}

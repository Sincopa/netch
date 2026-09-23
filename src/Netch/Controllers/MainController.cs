using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Microsoft.VisualStudio.Threading;
using Netch.Interfaces;
using Netch.Models;
using Netch.Models.Modes;
using Netch.Servers;
using Netch.Services;
using Netch.Utils;
using Netch.Application;

namespace Netch.Controllers;

public static class MainController
{
    public static Socks5Server? Socks5Server { get; private set; }

    public static Server? Server { get; private set; }

    public static Mode? Mode { get; private set; }

    public static IServerController? ServerController { get; private set; }

    public static IModeController? ModeController { get; private set; }

    private static readonly AsyncSemaphore Lock = new(1);

    public static async Task StartAsync(Server server, Mode mode, CancellationToken cancellationToken = default)
    {
        Exception? failure = null;
        using (await Lock.EnterAsync())
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Log.Information("Start MainController: {Server} {Mode}", $"{server.Type}", $"[{(int)mode.Type}]{mode.i18NRemark}");

                if (server.XrayProfile is null && await DnsUtils.LookupAsync(server.Hostname) == null)
                    throw new MessageException(i18N.Translate("Lookup Server hostname failed"));
                cancellationToken.ThrowIfCancellationRequested();

                // Cache the STUN server address before a connection can change DNS behavior.
                DnsUtils.LookupAsync(Global.Settings.STUN_Server).Forget();

                Server = server;
                Mode = mode;

                await Task.WhenAll(Task.Run(NativeMethods.RefreshDNSCache), Task.Run(Firewall.AddNetchFwRules));
                cancellationToken.ThrowIfCancellationRequested();

                ModeController = ModeService.GetModeControllerByType(mode.Type, out var modePort, out var portName);

                if (modePort != null)
                    TryReleaseTcpPort((ushort)modePort, portName);

                if (Server is Socks5Server socks5 && (!socks5.Auth() || ModeController.Features.HasFlag(ModeFeature.SupportSocks5Auth)))
                {
                    Socks5Server = socks5;
                }
                else
                {
                    // Start Server Controller to get a local socks5 server
                    Log.Debug("Server Information: {Data}", $"{server.Type} {server.MaskedData()}");

                    ServerController = new V2rayController
                    {
                        OutboundInterface = mode.Type == ModeType.TunMode ? NetworkInterfaceUtils.GetBest().Name : null
                    };
                    AppEvents.Publish("connection.statusChanged", new { message = i18N.TranslateFormat("Starting {0}", ServerController.Name) });

                    TryReleaseTcpPort(ServerController.Socks5LocalPort(), "Socks5");
                    Socks5Server = await ServerController.StartAsync(server);
                    cancellationToken.ThrowIfCancellationRequested();

                    StatusPortInfoText.Socks5Port = Socks5Server.Port;
                    StatusPortInfoText.UpdateShareLan();
                }

                // Start Mode Controller
                AppEvents.Publish("connection.statusChanged", new { message = i18N.TranslateFormat("Starting {0}", ModeController.Name) });

                await ModeController.StartAsync(Socks5Server, mode);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        if (failure is null)
            return;

        await StopAsync();
        switch (failure)
        {
            case OperationCanceledException:
                ExceptionDispatchInfo.Capture(failure).Throw();
                return;
            case MessageException:
                Log.Error("Connection start failed: {Reason}", failure.Message);
                ExceptionDispatchInfo.Capture(failure).Throw();
                return;
            case DllNotFoundException:
            case FileNotFoundException:
                throw new Exception(failure.Message + "\n\n" + i18N.Translate("Missing File or runtime components"), failure);
            default:
                Log.Error(failure, "Unhandled Exception When Start MainController");
                throw new MessageException($"{i18N.Translate("Unhandled Exception")}\n{failure.Message}");
        }
    }

    public static async Task StopAsync()
    {
        using var _ = await Lock.EnterAsync();

        if (ServerController == null && ModeController == null)
            return;

        Log.Information("Stop Main Controller");
        StatusPortInfoText.Reset();

        Exception? failure = null;
        try
        {
            if (ModeController != null) await ModeController.StopAsync();
        }
        catch (Exception exception)
        {
            failure = exception;
            Log.Error(exception, "Routing cleanup failed");
        }
        try
        {
            if (ServerController != null) await ServerController.StopAsync();
        }
        catch (Exception exception)
        {
            failure ??= exception;
            Log.Error(exception, "Proxy cleanup failed");
        }
        ServerController = null;
        ModeController = null;
        if (failure != null) throw new MessageException("Connection cleanup failed. Review the connection log.");
    }

    public static void PortCheck(ushort port, string portName, PortType portType = PortType.Both)
    {
        try
        {
            PortHelper.CheckPort(port, portType);
        }
        catch (PortInUseException)
        {
            throw new MessageException(i18N.TranslateFormat("The {0} port is in use.", $"{portName} ({port})"));
        }
        catch (PortReservedException)
        {
            throw new MessageException(i18N.TranslateFormat("The {0} port is reserved by system.", $"{portName} ({port})"));
        }
    }

    public static void TryReleaseTcpPort(ushort port, string portName)
    {
        foreach (var p in PortHelper.GetProcessByUsedTcpPort(port))
        {
            var fileName = p.MainModule?.FileName;
            if (fileName == null)
                continue;

            if (fileName.StartsWith(Global.NetchDir))
            {
                p.Kill();
                p.WaitForExit();
            }
            else
            {
                throw new MessageException(i18N.TranslateFormat("The {0} port is used by {1}.", $"{portName} ({port})", $"({p.Id}){fileName}"));
            }
        }

        PortCheck(port, portName, PortType.TCP);
    }

    public static Task<NatTypeTestResult> DiscoveryNatTypeAsync(CancellationToken ctx = default)
    {
        Debug.Assert(Socks5Server != null, nameof(Socks5Server) + " != null");
        return Socks5ServerTestUtils.DiscoveryNatTypeAsync(Socks5Server, ctx);
    }

    public static Task<int?> HttpConnectAsync(CancellationToken ctx = default)
    {
        Debug.Assert(Socks5Server != null, nameof(Socks5Server) + " != null");
        try
        {
            return Socks5ServerTestUtils.HttpConnectAsync(Socks5Server, ctx);
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        catch (Exception e)
        {
            Log.Warning(e, "Unhandled Socks5ServerTestUtils.HttpConnectAsync Exception");
        }

        return Task.FromResult<int?>(null);
    }
}

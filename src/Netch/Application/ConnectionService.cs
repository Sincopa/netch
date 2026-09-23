using Netch.Controllers;
using Netch.Enums;
using Netch.Models;
using Netch.Models.Modes;
using Netch.Utils;
using Microsoft.VisualStudio.Threading;

namespace Netch.Application;

public sealed class ConnectionService : IDisposable
{
    private readonly CatalogService _catalog;
    private readonly SemaphoreSlim _transition = new(1, 1);
    private readonly object _attemptLock = new();
    private string _status = "disconnected";
    private string _message = "Ready";
    private string? _serverId;
    private string? _modeId;
    private CancellationTokenSource? _bandwidthCancellation;
    private CancellationTokenSource? _connectCancellation;
    private DateTimeOffset? _connectedAt;

    public ConnectionService(CatalogService catalog)
    {
        _catalog = catalog;
    }

    public bool IsConnected => _status == "connected";

    public bool CanChangeSettings => _status is "disconnected" or "error";

    public async ValueTask<System.IAsyncDisposable> AcquireInactiveLeaseAsync(CancellationToken cancellationToken = default)
    {
        await _transition.WaitAsync(cancellationToken);
        if (_status is "disconnected" or "error")
            return new TransitionLease(_transition);

        _transition.Release();
        throw new AppException("CONNECTION_ACTIVE", "Disconnect before changing the server catalog.");
    }

    public ConnectionStateDto GetState() => new(_status, _message, _serverId, _modeId, _connectedAt);

    public async Task<ConnectionStateDto> ConnectAsync(string serverId, string modeId, CancellationToken cancellationToken = default)
    {
        await _transition.WaitAsync(cancellationToken);
        try
        {
            if (_status is "connecting" or "disconnecting")
                throw new AppException("CONNECTION_BUSY", "A connection transition is already running.");

            if (_status == "connected")
            {
                if (_serverId == serverId && _modeId == modeId)
                    return GetState();

                await StopCoreAsync();
            }
            return await StartCoreAsync(serverId, modeId, "connecting", cancellationToken);
        }
        finally
        {
            _transition.Release();
        }
    }

    public async Task<ConnectionStateDto> CancelConnectAsync(CancellationToken cancellationToken = default)
    {
        Task cancellationTask;
        lock (_attemptLock)
        {
            if (_status is not ("connecting" or "reconnecting") || _connectCancellation is null)
                throw new AppException("CONNECTION_NOT_CONNECTING", "There is no connection attempt to cancel.");
            cancellationTask = _connectCancellation.CancelAsync();
        }

        await cancellationTask;
        await _transition.WaitAsync(cancellationToken);
        _transition.Release();
        return GetState();
    }

    public async Task<ConnectionStateDto> ReconnectAsync(CancellationToken cancellationToken = default)
    {
        await _transition.WaitAsync(cancellationToken);
        try
        {
            if (_status is "connecting" or "disconnecting" or "reconnecting")
                throw new AppException("CONNECTION_BUSY", "A connection transition is already running.");

            var serverId = _serverId ?? _catalog.GetSelectedServerId();
            var modeId = _modeId ?? _catalog.GetSelectedModeId();
            if (serverId is null || modeId is null)
                throw new AppException("SELECTION_REQUIRED", "Select a server and a mode before reconnecting.");

            if (_status != "disconnected")
                await StopCoreAsync();
            return await StartCoreAsync(serverId, modeId, "reconnecting", cancellationToken);
        }
        finally
        {
            _transition.Release();
        }
    }

    public async Task<ConnectionStateDto> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _transition.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync();
            return GetState();
        }
        finally
        {
            _transition.Release();
        }
    }

    private async Task StopCoreAsync()
    {
        if (_status == "disconnected")
            return;

        SetState("disconnecting", "Stopping connection");
        if (_bandwidthCancellation is not null)
            await _bandwidthCancellation.CancelAsync();
        Bandwidth.Stop();
        try
        {
            await MainController.StopAsync();
        }
        catch (Exception exception)
        {
            SetState("error", "Connection cleanup failed");
            throw new AppException("DISCONNECT_FAILED", "Netch could not stop every connection component.", innerException: exception);
        }
        _connectedAt = null;
        SetState("disconnected", "Ready");
        PublishTraffic(0, 0);
    }

    private async Task<ConnectionStateDto> StartCoreAsync(
        string serverId,
        string modeId,
        string initialStatus,
        CancellationToken cancellationToken)
    {
        Server server = _catalog.ResolveServer(serverId);
        Mode mode = _catalog.ResolveMode(modeId);
        _serverId = serverId;
        _modeId = modeId;
        Global.Settings.ServerComboBoxSelectedIndex = int.Parse(serverId);
        Global.Settings.ModeComboBoxSelectedIndex = int.Parse(modeId);
        RoutingPreferences.RememberSelection();
        await Configuration.SaveAsync();

        var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_attemptLock)
            _connectCancellation = attempt;
        SetState(initialStatus, $"Starting {mode.i18NRemark}");
        try
        {
            await MainController.StartAsync(server, mode, attempt.Token);
            _connectedAt = DateTimeOffset.UtcNow;
            SetState("connected", $"Connected to {server.Remark}");
            StartBandwidthTracking();
            return GetState();
        }
        catch (OperationCanceledException)
        {
            await TryStopFailedStartAsync();
            _connectedAt = null;
            SetState("disconnected", "Connection cancelled");
            PublishTraffic(0, 0);
            return GetState();
        }
        catch (Exception exception)
        {
            await TryStopFailedStartAsync();
            _connectedAt = null;
            SetState("error", "Connection failed");
            var reason = exception is MessageException ? exception.Message : "The connection could not be established. Review the logs for details.";
            Log.Error("Connection failed: {Reason} ({ErrorType})", reason, exception.GetType().Name);
            throw new AppException("CONNECTION_FAILED", reason, innerException: exception);
        }
        finally
        {
            lock (_attemptLock)
            {
                if (ReferenceEquals(_connectCancellation, attempt))
                    _connectCancellation = null;
            }
            attempt.Dispose();
        }
    }

    private static async Task TryStopFailedStartAsync()
    {
        try
        {
            await MainController.StopAsync();
        }
        catch (Exception cleanupException)
        {
            Log.Warning(cleanupException, "Connection startup cleanup failed");
        }
    }

    private void StartBandwidthTracking()
    {
        _bandwidthCancellation?.Cancel();
        _bandwidthCancellation?.Dispose();
        _bandwidthCancellation = new CancellationTokenSource();
        var token = _bandwidthCancellation.Token;
        PublishTraffic(0, 0);

        Task.Run(() => Bandwidth.NetTraffic(
            () => !token.IsCancellationRequested,
            visible => AppEvents.Publish("traffic.visibilityChanged", new { visible }),
            PublishTraffic), token).Forget();

        if (MainController.ModeController is TUNController tun)
            MonitorTunAsync(tun, token).Forget();
    }

    private async Task MonitorTunAsync(TUNController tun, CancellationToken token)
    {
        try
        {
            await tun.ExitTask.WaitAsync(token);
            await _transition.WaitAsync(token);
            try
            {
                if (!token.IsCancellationRequested && ReferenceEquals(MainController.ModeController, tun) && IsConnected)
                {
                    Log.Error("TUN process stopped unexpectedly. See logging/tun2socks.log.");
                    await StopCoreAsync();
                    SetState("error", "TUN process stopped. See logging/tun2socks.log.");
                }
            }
            finally { _transition.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Log.Error(exception, "TUN process monitor failed"); }
    }

    private void PublishTraffic(ulong received, ulong sent)
    {
        var duration = _connectedAt is null
            ? 0
            : Math.Max(0, (long)(DateTimeOffset.UtcNow - _connectedAt.Value).TotalSeconds);
        AppEvents.Publish("traffic.updated", new TrafficSnapshotDto(
            received,
            sent,
            Bandwidth.Compute(received),
            Bandwidth.Compute(sent),
            duration,
            _connectedAt));
    }

    private void SetState(string status, string message)
    {
        _status = status;
        _message = message;
        AppEvents.Publish("connection.stateChanged", GetState());
    }

    public void Dispose()
    {
        lock (_attemptLock)
            _connectCancellation?.Cancel();
        _bandwidthCancellation?.Cancel();
        _bandwidthCancellation?.Dispose();
        _transition.Dispose();
    }

    private sealed class TransitionLease : System.IAsyncDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public TransitionLease(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                _semaphore.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}

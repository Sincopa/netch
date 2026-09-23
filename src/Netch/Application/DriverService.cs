using Netch.Controllers;

namespace Netch.Application;

public sealed class DriverService
{
    private readonly ConnectionService _connection;
    private readonly DiagnosticsService _diagnostics;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public DriverService(ConnectionService connection, DiagnosticsService diagnostics)
    {
        _connection = connection;
        _diagnostics = diagnostics;
    }

    public async Task<DiagnosticsSnapshotDto> InstallAsync(string driverId, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeDriverId(driverId);
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await _connection.AcquireInactiveLeaseAsync(cancellationToken);
            AppEvents.Publish("drivers.statusChanged", new { driverId = normalized, status = "installing" });
            try
            {
                await Task.Run(() =>
                {
                    if (normalized == "wintun")
                        TUNController.InstallDriver();
                    else
                        NFController.InstallDriver();
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                AppEvents.Publish("drivers.statusChanged", new { driverId = normalized, status = "idle" });
                throw;
            }
            catch (Exception exception)
            {
                AppEvents.Publish("drivers.statusChanged", new { driverId = normalized, status = "error" });
                throw new AppException("DRIVER_INSTALL_FAILED", $"{DriverName(normalized)} could not be installed.", exception.Message, exception);
            }

            var snapshot = await _diagnostics.GetSnapshotAsync(cancellationToken);
            AppEvents.Publish("diagnostics.changed", snapshot);
            AppEvents.Publish("notification", new { level = "success", message = $"{DriverName(normalized)} is ready." });
            return snapshot;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public static string NormalizeDriverId(string? value)
    {
        var id = value?.Trim().ToLowerInvariant();
        return id is "wintun" or "netfilter2"
            ? id
            : throw new AppException("INVALID_DRIVER", "Only Wintun and NetFilter2 can be installed.");
    }

    public static string DriverName(string id) => id == "wintun" ? "Wintun" : "NetFilter2 driver";
}

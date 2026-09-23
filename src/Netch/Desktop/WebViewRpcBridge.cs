using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Netch.Application;

namespace Netch.Desktop;

[Fody.ConfigureAwait(true)]
public sealed class WebViewRpcBridge : IDisposable
{
    private readonly WebView2 _webView;
    private readonly AppFacade _app;
    private readonly INativeWindowActions _window;
    private readonly HashSet<string> _allowedOrigins;
    private readonly JsonSerializerOptions _json;
    private readonly Dictionary<string, Func<JsonElement, CancellationToken, Task<object?>>> _handlers;
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;

    public WebViewRpcBridge(WebView2 webView, AppFacade app, INativeWindowActions window, IEnumerable<string> allowedOrigins)
    {
        _webView = webView;
        _app = app;
        _window = window;
        _allowedOrigins = new HashSet<string>(allowedOrigins.Select(NormalizeOrigin), StringComparer.OrdinalIgnoreCase);
        _json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        _json.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        _handlers = new(StringComparer.Ordinal)
        {
            ["app.bootstrap"] = (_, _) => ResultAsync(_app.GetBootstrap()),
            ["connection.getState"] = (_, _) => ResultAsync(_app.Connection.GetState()),
            ["connection.connect"] = async (data, token) =>
            {
                var request = Read<ConnectRequest>(data);
                return await _app.Connection.ConnectAsync(request.ServerId, request.ModeId, token);
            },
            ["connection.disconnect"] = async (_, token) => await _app.Connection.DisconnectAsync(token),
            ["connection.cancel"] = async (_, token) => await _app.Connection.CancelConnectAsync(token),
            ["connection.reconnect"] = async (_, token) => await _app.Connection.ReconnectAsync(token),
            ["servers.list"] = (_, _) => ResultAsync(_app.Catalog.GetServers()),
            ["servers.details"] = (_, _) => ResultAsync(_app.Servers.GetEditorDocument()),
            ["servers.saveDetails"] = async (data, token) => await _app.Servers.SaveDetailsAsync(Read<ServerDetailsWriteRequest>(data), token),
            ["servers.select"] = async (data, _) =>
            {
                EnsureSelectionMutable();
                await _app.Catalog.SelectServerAsync(Read<SelectRequest>(data).Id);
                AppEvents.Publish("routing.changed", _app.Processes.GetRouting());
                return _app.Catalog.GetServers();
            },
            ["servers.ping"] = async (data, token) => await _app.Catalog.PingAsync(Read<PingRequest>(data).ServerIds, token),
            ["servers.import"] = async (data, token) => await _app.Servers.ImportAsync(Read<ServerImportRequest>(data), token),
            ["servers.delete"] = async (data, token) => await _app.Servers.DeleteAsync(Read<ServerIdRequest>(data).Id, token),
            ["servers.setFavorite"] = async (data, token) => await _app.Servers.SetFavoriteAsync(Read<ServerFavoriteRequest>(data), token),
            ["modes.list"] = (_, _) => ResultAsync(_app.Catalog.GetModes()),
            ["modes.details"] = (_, _) => ResultAsync(_app.Modes.GetCatalog()),
            ["modes.save"] = async (data, token) => await _app.Modes.SaveAsync(Read<ModeWriteRequest>(data), token),
            ["modes.delete"] = async (data, token) => await _app.Modes.DeleteAsync(Read<ModeIdRequest>(data).Id, token),
            ["modes.select"] = async (data, _) =>
            {
                EnsureSelectionMutable();
                await _app.Catalog.SelectModeAsync(Read<SelectRequest>(data).Id);
                AppEvents.Publish("routing.changed", _app.Processes.GetRouting());
                return _app.Catalog.GetModes();
            },
            ["profiles.list"] = (_, _) => ResultAsync(_app.Profiles.GetAll()),
            ["profiles.save"] = async (data, token) => await _app.Profiles.SaveAsync(Read<ProfileWriteRequest>(data), token),
            ["profiles.delete"] = async (data, token) => await _app.Profiles.DeleteAsync(Read<ProfileSlotRequest>(data).Slot, token),
            ["profiles.activate"] = async (data, token) =>
            {
                var result = await _app.Profiles.ActivateAsync(Read<ProfileSlotRequest>(data).Slot, token);
                AppEvents.Publish("routing.changed", _app.Processes.GetRouting());
                return result;
            },
            ["subscriptions.list"] = (_, _) => ResultAsync(_app.Subscriptions.GetAll()),
            ["subscriptions.save"] = async (data, token) => await _app.Subscriptions.SaveAsync(Read<SubscriptionWriteRequest>(data), token),
            ["subscriptions.delete"] = async (data, token) => await _app.Subscriptions.DeleteAsync(Read<SubscriptionIdRequest>(data).Id, token),
            ["subscriptions.refresh"] = async (data, token) => await _app.Subscriptions.RefreshAsync(Read<SubscriptionIdRequest>(data).Id, token),
            ["subscriptions.refreshAll"] = async (_, token) => await _app.Subscriptions.RefreshAllAsync(token),
            ["processes.list"] = async (_, token) => await Task.Run(() => _app.Processes.GetRunning(token), token),
            ["processes.pickExecutable"] = async (_, token) =>
            {
                var path = await _window.PickExecutableAsync(token);
                return path is null ? null : _app.Processes.GetExecutable(path);
            },
            ["routing.get"] = (_, _) => ResultAsync(_app.Processes.GetRouting()),
            ["routing.setProcesses"] = async (data, token) => await _app.Processes.SaveSelectedAsync(Read<RoutingWriteRequest>(data).Processes, token),
            ["logs.get"] = (_, _) => ResultAsync(_app.Logs.GetEntries()),
            ["logs.clear"] = (_, _) =>
            {
                _app.Logs.Clear();
                return ResultAsync(true);
            },
            ["settings.get"] = (_, _) => ResultAsync(_app.Settings.GetDocument()),
            ["settings.update"] = async (data, token) =>
                await _app.Settings.UpdateAsync(Read<SettingsUpdateRequest>(data).Settings, token),
            ["diagnostics.get"] = async (_, token) => await _app.Diagnostics.GetSnapshotAsync(token),
            ["drivers.install"] = async (data, token) =>
            {
                var request = Read<DriverActionRequest>(data);
                var driverId = DriverService.NormalizeDriverId(request.DriverId);
                var name = DriverService.DriverName(driverId);
                if (!await _window.ConfirmDangerousActionAsync($"Install {name}?", $"Netch will install or repair {name}. Administrator access is required.", token))
                    throw new AppException("OPERATION_CANCELLED", "Driver installation was cancelled.");
                return await _app.Drivers.InstallAsync(driverId, token);
            },
            ["updates.getState"] = (_, _) => ResultAsync(_app.Updates.GetState()),
            ["updates.check"] = async (data, token) => await _app.Updates.CheckAsync(Read<UpdateCheckRequest>(data).IncludePrerelease, token),
            ["updates.download"] = async (_, token) => await _app.Updates.DownloadAsync(token),
            ["updates.apply"] = async (_, token) =>
            {
                if (!await _window.ConfirmDangerousActionAsync("Install Netch update?", "The active connection will stop, application files will be replaced, and Netch will restart.", token))
                    throw new AppException("OPERATION_CANCELLED", "Update installation was cancelled.");
                var state = await _app.Updates.ApplyAsync(token);
                _window.RequestRestart();
                return state;
            },
            ["window.beginDrag"] = (_, _) => WindowActionAsync(_window.BeginDrag),
            ["window.minimize"] = (_, _) => WindowActionAsync(_window.Minimize),
            ["window.toggleMaximize"] = (_, _) => WindowActionAsync(_window.ToggleMaximize),
            ["window.close"] = (_, _) => WindowActionAsync(_window.CloseWindow)
        };

        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        AppEvents.Published += OnAppEvent;
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        RpcRequest? request = null;
        try
        {
            if (!_allowedOrigins.Contains(NormalizeOrigin(args.Source)))
                throw new AppException("ORIGIN_DENIED", "The message origin is not allowed.");

            request = JsonSerializer.Deserialize<RpcRequest>(args.WebMessageAsJson, _json)
                ?? throw new AppException("INVALID_REQUEST", "The request body is empty.");

            if (string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.Method))
                throw new AppException("INVALID_REQUEST", "Request id and method are required.");

            if (!_handlers.TryGetValue(request.Method, out var handler))
                throw new AppException("METHOD_NOT_FOUND", $"Unknown method '{request.Method}'.");

            var result = await handler(request.Params, _lifetime.Token);
            Post(new RpcResponse(request.Id, result));
        }
        catch (OperationCanceledException)
        {
            if (request is not null)
                Post(new RpcResponse(request.Id, Error: new RpcError("CANCELLED", "The operation was cancelled.")));
        }
        catch (AppException exception)
        {
            if (request is not null)
                Post(new RpcResponse(request.Id, Error: new RpcError(exception.Code, exception.Message, exception.Details)));
        }
        catch (JsonException exception)
        {
            Post(new RpcResponse(request?.Id ?? string.Empty,
                Error: new RpcError("INVALID_PARAMS", "The request parameters are invalid.", exception.Message)));
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Unhandled WebView RPC error for {Method}", request?.Method);
            if (request is not null)
                Post(new RpcResponse(request.Id, Error: new RpcError("INTERNAL_ERROR", "The operation could not be completed.")));
        }
    }

    private void OnAppEvent(object? sender, AppEventMessage message)
    {
        if (_webView.IsDisposed || _lifetime.IsCancellationRequested)
            return;

        void Publish()
        {
            if (_webView.CoreWebView2 is null)
                return;

            _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
            {
                type = "event",
                @event = message.Name,
                payload = message.Payload
            }, _json));
        }

        if (_webView.InvokeRequired)
            _webView.BeginInvoke(Publish);
        else
            Publish();
    }

    private T Read<T>(JsonElement data)
    {
        if (data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new AppException("INVALID_PARAMS", "Request parameters are required.");

        return data.Deserialize<T>(_json) ?? throw new AppException("INVALID_PARAMS", "Request parameters are invalid.");
    }

    private void EnsureSelectionMutable()
    {
        if (!_app.Connection.CanChangeSettings)
            throw new AppException("CONNECTION_ACTIVE", "Disconnect before changing the active server or mode.");
    }

    private void Post(RpcResponse response)
    {
        _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(response, _json));
    }

    private static Task<object?> ResultAsync(object? value) => Task.FromResult(value);

    private static Task<object?> WindowActionAsync(Action action)
    {
        action();
        return ResultAsync(true);
    }

    private static string NormalizeOrigin(string source)
    {
        return Uri.TryCreate(source, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority)
            : string.Empty;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _lifetime.Cancel();
        AppEvents.Published -= OnAppEvent;
        if (_webView.CoreWebView2 is not null)
            _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        _lifetime.Dispose();
    }
}

using Netch.Models;
using Netch.Utils;

namespace Netch.Application;

public sealed class SubscriptionService
{
    private readonly ConnectionService _connection;
    private readonly object _stateLock = new();
    private readonly Dictionary<Subscription, RefreshState> _states = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public SubscriptionService(ConnectionService connection)
    {
        _connection = connection;
    }

    public IReadOnlyList<SubscriptionDto> GetAll()
    {
        return Global.Settings.Subscription.Select((item, index) => ToDto(item, index)).ToArray();
    }

    public async Task<IReadOnlyList<SubscriptionDto>> SaveAsync(SubscriptionWriteRequest request, CancellationToken cancellationToken = default)
    {
        var uri = ValidateUrl(request.Url);
        var remark = request.Remark?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(remark) || remark.Length > 64 || remark.Any(char.IsControl))
            throw new AppException("INVALID_SUBSCRIPTION", "Subscription name must contain 1 to 64 visible characters.");
        var userAgent = request.UserAgent?.Trim() ?? string.Empty;
        if (userAgent.Length > 512 || userAgent.Any(char.IsControl))
            throw new AppException("INVALID_SUBSCRIPTION", "User agent cannot exceed 512 visible characters.");

        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await _connection.AcquireInactiveLeaseAsync(cancellationToken);
            Subscription item;
            var created = request.Id is null;
            if (created)
            {
                if (Global.Settings.Subscription.Any(value => string.Equals(value.Remark, remark, StringComparison.OrdinalIgnoreCase)))
                    throw new AppException("SUBSCRIPTION_EXISTS", "A subscription with this name already exists.");
                item = new Subscription();
                Global.Settings.Subscription.Add(item);
            }
            else
                item = Resolve(request.Id!);

            var previous = (item.Remark, item.Link, item.UserAgent, item.Enable);
            var affected = Global.Settings.Server.Where(server => server.Group == previous.Remark).ToArray();
            if (!string.Equals(previous.Remark, remark, StringComparison.Ordinal))
                foreach (var server in affected)
                    server.Group = remark;
            item.Remark = remark;
            item.Link = uri.AbsoluteUri;
            item.UserAgent = userAgent;
            item.Enable = request.Enabled;

            try
            {
                await Configuration.SaveAsync();
            }
            catch (Exception exception)
            {
                if (created)
                    Global.Settings.Subscription.Remove(item);
                else
                    (item.Remark, item.Link, item.UserAgent, item.Enable) = previous;
                foreach (var server in affected)
                    server.Group = previous.Remark;
                throw new AppException("SUBSCRIPTION_SAVE_FAILED", "The subscription could not be saved.", innerException: exception);
            }
            AppEvents.Publish("subscriptions.changed", GetAll());
            if (affected.Length > 0)
                AppEvents.Publish("servers.changed", true);
            return GetAll();
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await _connection.AcquireInactiveLeaseAsync(cancellationToken);
            var item = Resolve(id);
            var subscriptionIndex = Global.Settings.Subscription.IndexOf(item);
            var previousServers = Global.Settings.Server.ToList();
            var selectedServer = GetSelectedServer();
            Global.Settings.Server.RemoveAll(server => server.Group == item.Remark);
            Global.Settings.Subscription.Remove(item);
            RestoreSelection(selectedServer);
            try
            {
                await Configuration.SaveAsync();
            }
            catch (Exception exception)
            {
                Global.Settings.Subscription.Insert(subscriptionIndex, item);
                Global.Settings.Server.Clear();
                Global.Settings.Server.AddRange(previousServers);
                RestoreSelection(selectedServer);
                throw new AppException("SUBSCRIPTION_DELETE_FAILED", "The subscription could not be deleted.", innerException: exception);
            }
            lock (_stateLock)
                _states.Remove(item);
            AppEvents.Publish("subscriptions.changed", GetAll());
            AppEvents.Publish("servers.changed", true);
            return GetAll();
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task<int> RefreshAsync(string id, CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await _connection.AcquireInactiveLeaseAsync(cancellationToken);
            var previousServers = Global.Settings.Server.ToList();
            var selectedServer = GetSelectedServer();
            var item = Resolve(id);
            var result = await RefreshItemAsync(item, cancellationToken);
            RestoreSelection(selectedServer);
            try
            {
                await Configuration.SaveAsync();
            }
            catch (Exception exception)
            {
                Global.Settings.Server.Clear();
                Global.Settings.Server.AddRange(previousServers);
                RestoreSelection(selectedServer);
                throw new AppException("SUBSCRIPTION_SAVE_FAILED", "The refreshed subscription data could not be saved.", innerException: exception);
            }
            AppEvents.Publish("servers.changed", true);
            if (result.Error is not null)
                throw result.Error;

            AppEvents.Publish("subscriptions.refreshed", new { id, count = result.ServerCount });
            return result.ServerCount;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task<SubscriptionRefreshSummaryDto> RefreshAllAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await _connection.AcquireInactiveLeaseAsync(cancellationToken);
            var previousServers = Global.Settings.Server.ToList();
            var selectedServer = GetSelectedServer();
            var items = Global.Settings.Subscription.Where(item => item.Enable).ToArray();
            using var concurrency = new SemaphoreSlim(4);
            var results = await Task.WhenAll(items.Select(async item =>
            {
                await concurrency.WaitAsync(cancellationToken);
                try
                {
                    return await RefreshItemAsync(item, cancellationToken);
                }
                finally
                {
                    concurrency.Release();
                }
            }));

            RestoreSelection(selectedServer);
            try
            {
                await Configuration.SaveAsync();
            }
            catch (Exception exception)
            {
                Global.Settings.Server.Clear();
                Global.Settings.Server.AddRange(previousServers);
                RestoreSelection(selectedServer);
                throw new AppException("SUBSCRIPTION_SAVE_FAILED", "The refreshed subscription data could not be saved.", innerException: exception);
            }
            AppEvents.Publish("servers.changed", true);

            var summary = new SubscriptionRefreshSummaryDto(
                results.Sum(result => result.ServerCount),
                results.Count(result => result.Error is null),
                results.Count(result => result.Error is not null));
            AppEvents.Publish("subscriptions.refreshed", summary);
            return summary;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private static Uri ValidateUrl(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 4096 || normalized.Any(char.IsControl) ||
            !Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new AppException("INVALID_SUBSCRIPTION_URL", "Subscription URL must use HTTP or HTTPS.");

        return uri;
    }

    private static Subscription Resolve(string id)
    {
        if (!int.TryParse(id, out var index) || index < 0 || index >= Global.Settings.Subscription.Count)
            throw new AppException("SUBSCRIPTION_NOT_FOUND", "The subscription no longer exists.");

        return Global.Settings.Subscription[index];
    }

    private static Server? GetSelectedServer()
    {
        var index = Global.Settings.ServerComboBoxSelectedIndex;
        return index >= 0 && index < Global.Settings.Server.Count ? Global.Settings.Server[index] : null;
    }

    private static void RestoreSelection(Server? previous)
    {
        if (Global.Settings.Server.Count == 0)
        {
            Global.Settings.ServerComboBoxSelectedIndex = -1;
            return;
        }
        if (previous is null)
        {
            Global.Settings.ServerComboBoxSelectedIndex = 0;
            return;
        }
        var index = Global.Settings.Server.IndexOf(previous);
        if (index < 0)
            index = Global.Settings.Server.FindIndex(server =>
                server.Type == previous.Type && server.Hostname == previous.Hostname && server.Port == previous.Port);
        Global.Settings.ServerComboBoxSelectedIndex = index >= 0 ? index : 0;
    }

    private SubscriptionDto ToDto(Subscription item, int index)
    {
        RefreshState state;
        lock (_stateLock)
            state = _states.GetValueOrDefault(item) ?? RefreshState.Idle;

        return new SubscriptionDto(
            index.ToString(),
            item.Remark,
            item.Link,
            item.UserAgent,
            item.Enable,
            SubscriptionUtil.CountServers(item.Remark),
            state.Status,
            state.LastUpdatedAt,
            state.LastError);
    }

    private async Task<RefreshResult> RefreshItemAsync(Subscription item, CancellationToken cancellationToken)
    {
        SetState(item, "refreshing");
        PublishSubscriptions();
        try
        {
            var count = await SubscriptionUtil.UpdateServerAsync(item, cancellationToken: cancellationToken);
            SetState(item, "success", DateTimeOffset.UtcNow);
            PublishSubscriptions();
            return new RefreshResult(count, null);
        }
        catch (OperationCanceledException)
        {
            SetState(item, "idle");
            PublishSubscriptions();
            throw;
        }
        catch (AppException exception)
        {
            SetState(item, "error", error: exception.Message);
            PublishSubscriptions();
            return new RefreshResult(0, exception);
        }
    }

    private void SetState(Subscription item, string status, DateTimeOffset? updatedAt = null, string? error = null)
    {
        lock (_stateLock)
        {
            var current = _states.GetValueOrDefault(item) ?? RefreshState.Idle;
            _states[item] = new RefreshState(status, updatedAt ?? current.LastUpdatedAt, error);
        }
    }

    private void PublishSubscriptions() => AppEvents.Publish("subscriptions.changed", GetAll());

    private sealed record RefreshState(string Status, DateTimeOffset? LastUpdatedAt, string? LastError)
    {
        public static readonly RefreshState Idle = new("idle", null, null);
    }

    private sealed record RefreshResult(int ServerCount, AppException? Error);
}

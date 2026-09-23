using System.Net;
using Netch.Application;
using Netch.Models;

namespace Netch.Utils;

public static class SubscriptionUtil
{
    private static readonly object ServerLock = new();

    public static int CountServers(string group)
    {
        lock (ServerLock)
            return Global.Settings.Server.Count(server => CatalogService.HasEndpoint(server) && string.Equals(server.Group, group, StringComparison.Ordinal));
    }

    public static Task<int[]> UpdateServersAsync(string? proxyServer = default, CancellationToken cancellationToken = default)
    {
        return Task.WhenAll(Global.Settings.Subscription.Where(item => item.Enable)
            .Select(item => UpdateServerAsync(item, proxyServer, cancellationToken)));
    }

    public static async Task<int> UpdateServerAsync(Subscription item, string? proxyServer = default, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!item.Enable)
                return 0;

            cancellationToken.ThrowIfCancellationRequested();

            var request = WebUtil.CreateRequest(item.Link, userAgent: "v2ray");

            if (!string.IsNullOrEmpty(item.UserAgent))
                request = request with { UserAgent = item.UserAgent };

            if (!string.IsNullOrEmpty(proxyServer))
                request.Proxy = new WebProxy(proxyServer);

            List<Server> servers;

            var (code, result) = await WebUtil.DownloadStringAsync(request, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (code == HttpStatusCode.OK)
                servers = ShareLink.ParseText(result);
            else
                throw new Exception($"{item.Remark} Response Status Code: {code}");

            foreach (var server in servers)
                server.Group = item.Remark;

            // Never replace a working subscription with an HTML/error/unsupported response.
            if (servers.Count == 0)
                throw new AppException("SUBSCRIPTION_EMPTY", "The subscription returned no supported servers. Existing servers were kept.");

            lock (ServerLock)
            {
                Global.Settings.Server.RemoveAll(server => string.Equals(server.Group, item.Remark, StringComparison.Ordinal));
                Global.Settings.Server.AddRange(servers);
            }

            AppEvents.Publish("notification", new
            {
                level = "success",
                message = i18N.TranslateFormat("Update {1} server(s) from {0}", item.Remark, servers.Count)
            });
            return servers.Count;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            var safeMessage = RedactSubscriptionUrl(e.Message, item.Link);
            Log.Warning("Update servers failed for {Subscription}: {ErrorType}: {Reason}", item.Remark, e.GetType().Name, safeMessage);
            AppEvents.Publish("notification", new
            {
                level = "error",
                message = $"{i18N.TranslateFormat("Update servers failed from {0}", item.Remark)}\n{safeMessage}"
            });
            throw new AppException("SUBSCRIPTION_UPDATE_FAILED", $"Failed to update subscription '{item.Remark}'.", safeMessage, e);
        }
    }

    private static string RedactSubscriptionUrl(string message, string url)
    {
        if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(url))
            return message;

        return message.Replace(url, "[subscription URL]", StringComparison.OrdinalIgnoreCase);
    }
}

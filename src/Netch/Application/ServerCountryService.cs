using System.Net;
using MaxMind.GeoIP2;
using Netch.Utils;

namespace Netch.Application;

public sealed class ServerCountryService : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _runGate = new(1, 1);

    public async Task WarmAsync()
    {
        var cancellationToken = _lifetime.Token;
        if (!await _runGate.WaitAsync(0, cancellationToken))
            return;
        try
        {
            var databasePath = Path.Combine(Global.NetchDir, "bin", "GeoLite2-Country.mmdb");
            if (!File.Exists(databasePath))
                return;
            var hosts = Global.Settings.Server
                .Where(CatalogService.HasEndpoint)
                .Select(server => server.Hostname)
                .Where(host => ServerIdentity.ResolveCountryCode(string.Empty, string.Empty, host) is null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2000)
                .ToArray();
            if (hosts.Length == 0)
                return;

            using var reader = new DatabaseReader(databasePath);
            using var concurrency = new SemaphoreSlim(4, 4);
            await Task.WhenAll(hosts.Select(async host =>
            {
                await concurrency.WaitAsync(cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var address = IPAddress.TryParse(host, out var parsed) ? parsed : await DnsUtils.LookupAsync(host);
                    if (address is null)
                        return;
                    var country = reader.Country(address).Country.IsoCode;
                    if (string.IsNullOrWhiteSpace(country))
                        return;
                    ServerIdentity.CacheGeoIpCountry(host, country);
                    AppEvents.Publish("servers.countryChanged", new { hostname = host, countryCode = country.ToUpperInvariant() });
                }
                catch (OperationCanceledException)
                {
                    // Application is shutting down.
                }
                catch (Exception exception)
                {
                    Log.Debug(exception, "GeoIP lookup failed for {Hostname}", host);
                }
                finally
                {
                    concurrency.Release();
                }
            }));
        }
        catch (OperationCanceledException)
        {
            // Application is shutting down.
        }
        finally
        {
            _runGate.Release();
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}

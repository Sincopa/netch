using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Netch.Utils;

public static class WebUtil
{
    public const string DefaultUserAgent =
        @"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/94.0.4606.61 Safari/537.36 Edg/94.0.992.31";

    static WebUtil()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
    }

    private static int DefaultGetTimeout => Global.Settings.RequestTimeout;

    public static RequestOptions CreateRequest(string url, int? timeout = null, string? userAgent = null)
    {
        return new RequestOptions(
            new Uri(url, UriKind.Absolute),
            string.IsNullOrWhiteSpace(userAgent) ? DefaultUserAgent : userAgent,
            Math.Clamp(timeout ?? DefaultGetTimeout, 1_000, 120_000));
    }

    public static async Task<byte[]> DownloadBytesAsync(RequestOptions request, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(request);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);
        try
        {
            using var response = await client.GetAsync(request.Address, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync(timeout.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The HTTP request timed out.", exception);
        }
    }

    public static async Task<(HttpStatusCode, string)> DownloadStringAsync(
        RequestOptions request,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(request);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);
        try
        {
            using var response = await client.GetAsync(request.Address, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var content = encoding is null
                ? await response.Content.ReadAsStringAsync(timeout.Token)
                : encoding.GetString(await response.Content.ReadAsByteArrayAsync(timeout.Token));
            return (response.StatusCode, content);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The HTTP request timed out.", exception);
        }
    }

    public static Task DownloadFileAsync(
        string address,
        string fileFullPath,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return DownloadFileAsync(CreateRequest(address), fileFullPath, progress, cancellationToken);
    }

    public static async Task DownloadFileAsync(
        RequestOptions request,
        string fileFullPath,
        IProgress<int>? progress,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(request);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);
        try
        {
            using var response = await client.GetAsync(request.Address, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            await using var output = new FileStream(fileFullPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            var total = response.Content.Headers.ContentLength;
            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                received += read;
                if (total > 0)
                    progress?.Report((int)Math.Clamp(received * 100L / total.Value, 0, 99));
            }

            progress?.Report(100);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The HTTP request timed out.", exception);
        }
    }

    private static HttpClient CreateClient(RequestOptions request)
    {
        var handler = new HttpClientHandler();
        if (request.Proxy is not null)
        {
            handler.Proxy = request.Proxy;
            handler.UseProxy = true;
        }

        var client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Charset", "utf-8");
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", request.UserAgent);
        return client;
    }

    public sealed record RequestOptions(Uri Address, string UserAgent, int Timeout)
    {
        public IWebProxy? Proxy { get; set; }
    }
}

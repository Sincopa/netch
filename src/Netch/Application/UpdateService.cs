using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Netch.Controllers;
using Netch.Models.GitHubRelease;
using Netch.Services;
using Netch.Utils;

namespace Netch.Application;

public sealed class UpdateService : IAsyncDisposable
{
    private const long MaxPackageBytes = 1_073_741_824;
    private static readonly Uri ReleasesUri = new($"https://api.github.com/repos/{UpdateChecker.Owner}/{UpdateChecker.Repo}/releases");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConnectionService _connection;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HttpClient _httpClient;
    private UpdateStateDto _state = IdleState();
    private Release? _release;
    private UpdatePackage? _package;
    private string? _downloadedFile;
    private int _disposed;

    public UpdateService(ConnectionService connection, HttpMessageHandler? handler = null)
    {
        _connection = connection;
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler, true);
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Netch", UpdateChecker.AssemblyVersion));
    }

    public UpdateStateDto GetState() => _state;

    public async Task<UpdateStateDto> CheckAsync(bool includePrerelease, CancellationToken cancellationToken = default)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        cancellationToken = operation.Token;
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            SetState(_state with { Status = "checking", Progress = 0, Error = null, CanDownload = false, CanApply = false });
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUri);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var releases = await JsonSerializer.DeserializeAsync<List<Release>>(stream, JsonOptions, cancellationToken) ?? new List<Release>();
                _release = SelectLatestRelease(releases, includePrerelease);
                if (_release is null)
                    throw new AppException("UPDATE_RELEASE_NOT_FOUND", "No compatible release was returned by the update service.");

                var available = VersionUtil.CompareVersion(_release.tag_name, UpdateChecker.Version) > 0;
                _package = available ? ResolvePackage(_release) : null;
                _downloadedFile = null;
                SetState(new UpdateStateDto(
                    available ? "available" : "up-to-date",
                    UpdateChecker.Version,
                    _release.tag_name,
                    NormalizeReleaseNotes(_release.body),
                    HttpsUrlOrNull(_release.html_url),
                    0,
                    null,
                    available && _package is not null,
                    false));
                if (available)
                    AppEvents.Publish("notification", new { level = "info", message = $"Netch {_release.tag_name} is available." });
                return _state;
            }
            catch (OperationCanceledException)
            {
                SetState(IdleState());
                throw;
            }
            catch (AppException exception)
            {
                SetError(exception.Message);
                throw;
            }
            catch (Exception exception)
            {
                SetError("The update service could not be reached.");
                throw new AppException("UPDATE_CHECK_FAILED", "The update service could not be reached.", exception.Message, exception);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<UpdateStateDto> DownloadAsync(CancellationToken cancellationToken = default)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        cancellationToken = operation.Token;
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            if (_release is null || _package is null || _state.Status is not ("available" or "error"))
                throw new AppException("UPDATE_NOT_AVAILABLE", "Check for an available update before downloading it.");

            var updateDirectory = Path.Combine(Global.NetchDir, "data", "updates");
            Directory.CreateDirectory(updateDirectory);
            var destination = Path.Combine(updateDirectory, _package.FileName);
            var temporary = destination + ".download";
            SetState(_state with { Status = "downloading", Progress = 0, Error = null, CanDownload = false, CanApply = false });
            try
            {
                using var response = await _httpClient.GetAsync(_package.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                var length = response.Content.Headers.ContentLength ?? _package.Size;
                if (length is <= 0 or > MaxPackageBytes)
                    throw new AppException("UPDATE_SIZE_INVALID", "The update package size is invalid.");

                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920];
                    long received = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        received += read;
                        if (received > MaxPackageBytes)
                            throw new AppException("UPDATE_SIZE_INVALID", "The update package exceeds the allowed size.");
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        ReportProgress(length, received);
                    }
                }

                var actualHash = await Utils.Utils.Sha256CheckSumAsync(temporary);
                if (!string.Equals(actualHash, _package.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new AppException("UPDATE_HASH_MISMATCH", "The downloaded update package failed SHA-256 verification.");

                File.Move(temporary, destination, true);
                _downloadedFile = destination;
                SetState(_state with { Status = "ready", Progress = 100, Error = null, CanApply = true });
                return _state;
            }
            catch (OperationCanceledException)
            {
                TryDelete(temporary);
                SetState(_state with { Status = "available", Progress = 0, Error = null, CanDownload = true, CanApply = false });
                throw;
            }
            catch (AppException exception)
            {
                TryDelete(temporary);
                SetError(exception.Message, canDownload: true);
                throw;
            }
            catch (Exception exception)
            {
                TryDelete(temporary);
                SetError("The update package could not be downloaded.", canDownload: true);
                throw new AppException("UPDATE_DOWNLOAD_FAILED", "The update package could not be downloaded.", exception.Message, exception);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<UpdateStateDto> ApplyAsync(CancellationToken cancellationToken = default)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        cancellationToken = operation.Token;
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            if (_package is null || string.IsNullOrWhiteSpace(_downloadedFile) || !File.Exists(_downloadedFile) || !_state.CanApply)
                throw new AppException("UPDATE_NOT_READY", "Download and verify the update before applying it.");

            var actualHash = await Utils.Utils.Sha256CheckSumAsync(_downloadedFile);
            if (!string.Equals(actualHash, _package.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new AppException("UPDATE_HASH_MISMATCH", "The update package failed its final SHA-256 verification.");

            SetState(_state with { Status = "applying", Error = null, CanApply = false });
            try
            {
                await _connection.DisconnectAsync(cancellationToken);
                await Configuration.SaveAsync();
                var updater = new Updater(_downloadedFile, Global.NetchDir);
                await Task.Run(updater.ApplyUpdate, cancellationToken);
                SetState(_state with { Status = "applied", Progress = 100 });
                return _state;
            }
            catch (OperationCanceledException)
            {
                SetState(_state with { Status = "ready", CanApply = true });
                throw;
            }
            catch (Exception exception)
            {
                SetError("The update could not be applied.", canApply: true);
                throw new AppException("UPDATE_APPLY_FAILED", "The update could not be applied.", exception.Message, exception);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public static Release? SelectLatestRelease(IEnumerable<Release> releases, bool includePrerelease)
    {
        var eligible = releases.Where(release => !release.draft && (includePrerelease || !release.prerelease));
        return eligible.OrderByDescending(release => release.tag_name, new VersionUtil.VersionComparer()).FirstOrDefault();
    }

    public static UpdatePackage? ResolvePackage(Release release)
    {
        var hashes = Regex.Matches(release.body ?? string.Empty, @"^\|\s*(?<file>[^|]+?)\s*\|\s*(?<hash>[a-fA-F0-9]{64})\s*\|\s*$", RegexOptions.Multiline)
            .Cast<Match>()
            .GroupBy(match => match.Groups["file"].Value.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Groups["hash"].Value.ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);

        foreach (var asset in release.assets ?? Array.Empty<Asset>())
        {
            var fileName = asset.name?.Trim();
            if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName || !hashes.TryGetValue(fileName, out var hash))
                continue;
            if (!Uri.TryCreate(asset.browser_download_url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                continue;
            if (asset.size is <= 0 || asset.size > MaxPackageBytes)
                continue;
            return new UpdatePackage(fileName, hash, uri, asset.size);
        }

        return null;
    }

    private static UpdateStateDto IdleState() => new("idle", UpdateChecker.Version, null, null, null, 0, null, false, false);

    private void ReportProgress(long total, long received)
    {
        var progress = (int)Math.Clamp(received * 100L / total, 0, 100);
        if (progress == _state.Progress)
            return;
        SetState(_state with { Progress = progress });
    }

    private void SetError(string message, bool canDownload = false, bool canApply = false)
    {
        SetState(_state with { Status = "error", Error = message, CanDownload = canDownload, CanApply = canApply });
    }

    private void SetState(UpdateStateDto state)
    {
        _state = state;
        AppEvents.Publish("updates.changed", state);
    }

    private static string NormalizeReleaseNotes(string? value)
    {
        var notes = value?.Trim() ?? string.Empty;
        return notes.Length <= 20_000 ? notes : notes[..20_000] + "\n…";
    }

    private static string? HttpsUrlOrNull(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.AbsoluteUri : null;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Could not delete partial update package");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _lifetime.CancelAsync();
        await _operationGate.WaitAsync();
        _httpClient.Dispose();
        _operationGate.Dispose();
        _lifetime.Dispose();
    }
}

public sealed record UpdatePackage(string FileName, string Sha256, Uri DownloadUri, long Size);

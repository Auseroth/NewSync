using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace NewSync.App.Services;

public sealed class GitHubUpdaterOptions
{
    public string ReleasesApiUrl { get; set; } = string.Empty;
    public Version CurrentVersion { get; set; } = new(1, 0, 0);
    public string DownloadDirectory { get; set; } = Path.GetTempPath();
    public string UserAgent { get; set; } = "NewSync-Updater";
    public string AssetNameFilter { get; set; } = ".exe";
    public int AutoCheckIntervalHours { get; set; } = 24;
}

public sealed class UpdateCheckResult
{
    public bool UpdateAvailable { get; init; }
    public string TagName { get; init; } = string.Empty;
    public Version? LatestVersion { get; init; }
    public string AssetDownloadUrl { get; init; } = string.Empty;
    public string AssetFileName { get; init; } = string.Empty;
    public string ReleaseNotes { get; init; } = string.Empty;
}

public sealed class GitHubUpdater : IDisposable
{
    private readonly GitHubUpdaterOptions _options;
    private readonly HttpClient _http;
    private System.Threading.Timer? _timer;

    public event EventHandler<UpdateCheckResult>? UpdateAvailable;

    public GitHubUpdater(GitHubUpdaterOptions options)
    {
        _options = options;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);
    }

    public async Task<UpdateCheckResult?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ReleasesApiUrl))
        {
            return null;
        }

        try
        {
            using var response = await _http.GetAsync(_options.ReleasesApiUrl, ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            GitHubRelease? release = null;
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                release = JsonSerializer.Deserialize<GitHubRelease>(doc.RootElement.GetRawText());
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var releases = JsonSerializer.Deserialize<GitHubRelease[]>(doc.RootElement.GetRawText()) ?? [];
                release = releases.FirstOrDefault(r => r is not null && !r.Draft && !r.PreRelease)
                    ?? releases.FirstOrDefault();
            }

            if (release is null)
            {
                return null;
            }

            if (!TryParseVersionTag(release.TagName, out var latest))
            {
                return null;
            }

            var asset = release.Assets?.FirstOrDefault(a => (a.Name ?? string.Empty)
                .EndsWith(_options.AssetNameFilter, StringComparison.OrdinalIgnoreCase));

            var result = new UpdateCheckResult
            {
                UpdateAvailable = latest > _options.CurrentVersion,
                TagName = release.TagName ?? string.Empty,
                LatestVersion = latest,
                AssetDownloadUrl = asset?.BrowserDownloadUrl ?? string.Empty,
                AssetFileName = asset?.Name ?? string.Empty,
                ReleaseNotes = release.Body ?? string.Empty
            };

            if (result.UpdateAvailable)
            {
                UpdateAvailable?.Invoke(this, result);
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseVersionTag(string? tagName, out Version latest)
    {
        latest = new Version(0, 0, 0, 0);

        var tag = (tagName ?? string.Empty).Trim().TrimStart('v', 'V');
        if (Version.TryParse(tag, out var parsed) && parsed is not null)
        {
            latest = parsed;
            return true;
        }

        var match = Regex.Match(tag, @"\d+(?:\.\d+){1,3}");
        if (match.Success && Version.TryParse(match.Value, out parsed) && parsed is not null)
        {
            latest = parsed;
            return true;
        }

        return false;
    }

    public async Task<string?> DownloadInstallerAsync(UpdateCheckResult result, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(result.AssetDownloadUrl) || string.IsNullOrWhiteSpace(result.AssetFileName))
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(_options.DownloadDirectory);
            var destination = Path.Combine(_options.DownloadDirectory, result.AssetFileName);

            using var response = await _http.GetAsync(result.AssetDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var length = response.Content.Headers.ContentLength ?? -1;
            await using var inStream = await response.Content.ReadAsStreamAsync(ct);
            await using var outStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalRead = 0;
            int read;
            while ((read = await inStream.ReadAsync(buffer, ct)) > 0)
            {
                await outStream.WriteAsync(buffer.AsMemory(0, read), ct);
                totalRead += read;
                if (length > 0)
                {
                    progress?.Report((double)totalRead / length);
                }
            }

            return destination;
        }
        catch
        {
            return null;
        }
    }

    public void LaunchInstaller(string installerPath)
    {
        if (!File.Exists(installerPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true
        });
    }

    public void StartAutoChecks()
    {
        if (_options.AutoCheckIntervalHours <= 0)
        {
            return;
        }

        StopAutoChecks();
        var interval = TimeSpan.FromHours(_options.AutoCheckIntervalHours);
        _timer = new System.Threading.Timer(_ => _ = CheckForUpdateAsync(), null, interval, interval);
    }

    public void StopAutoChecks()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose()
    {
        StopAutoChecks();
        _http.Dispose();
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool PreRelease { get; set; }
        [JsonPropertyName("assets")] public GitHubAsset[]? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }
}

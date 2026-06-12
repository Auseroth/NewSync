using System.Reflection;
using NewSync.App.Models;

namespace NewSync.App.Services;

public sealed class UpdateService : IDisposable
{
    private readonly LoggingService _logger;
    private GitHubUpdater? _updater;

    public event EventHandler<UpdateCheckResult>? UpdateAvailable;

    public UpdateService(LoggingService logger)
    {
        _logger = logger;
    }

    public void Configure(AppConfig appConfig)
    {
        _updater?.Dispose();

        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        _updater = new GitHubUpdater(new GitHubUpdaterOptions
        {
            ReleasesApiUrl = string.IsNullOrWhiteSpace(appConfig.Updates.GithubReleasesUrl)
                ? ResolveDefaultReleasesUrl() ?? string.Empty
                : appConfig.Updates.GithubReleasesUrl,
            CurrentVersion = version,
            DownloadDirectory = Path.Combine(AppPaths.LocalDataDir, "updates"),
            UserAgent = "NewSync",
            AssetNameFilter = ".exe",
            AutoCheckIntervalHours = 0
        });
        _updater.UpdateAvailable += (_, result) => UpdateAvailable?.Invoke(this, result);
    }

    public async Task<UpdateCheckResult?> CheckNowAsync(CancellationToken ct = default)
    {
        if (_updater is null)
        {
            return null;
        }

        var result = await _updater.CheckForUpdateAsync(ct);
        if (result?.UpdateAvailable == true)
        {
            _logger.Info($"Update available: {result.TagName}");
        }

        return result;
    }

    public async Task<bool> DownloadAndLaunchAsync(
        UpdateCheckResult result,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (_updater is null)
        {
            return false;
        }

        var installerPath = await _updater.DownloadInstallerAsync(result, progress, ct);
        if (string.IsNullOrWhiteSpace(installerPath))
        {
            return false;
        }

        _updater.LaunchInstaller(installerPath);
        return true;
    }

    public void Dispose()
    {
        _updater?.Dispose();
    }

    public static string? ResolveDefaultReleasesUrl()
    {
        var repoUrl = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositoryUrl")?.Value;

        if (string.IsNullOrWhiteSpace(repoUrl))
        {
            return null;
        }

        var uri = new Uri(repoUrl.TrimEnd('/'));
        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var path = uri.AbsolutePath.Trim('/');
        return $"https://api.github.com/repos/{path}/releases";
    }
}

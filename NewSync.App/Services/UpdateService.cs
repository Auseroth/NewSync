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
            ReleasesApiUrl = appConfig.Updates.GithubReleasesUrl,
            CurrentVersion = version,
            DownloadDirectory = Path.Combine(AppPaths.LocalDataDir, "updates"),
            UserAgent = "NewSync",
            AssetNameFilter = ".exe",
            AutoCheckIntervalHours = appConfig.Updates.AutoUpdateIntervalHours
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

    public void StartAutoChecks()
    {
        _updater?.StartAutoChecks();
    }

    public async Task<bool> DownloadAndLaunchAsync(UpdateCheckResult result, CancellationToken ct = default)
    {
        if (_updater is null)
        {
            return false;
        }

        var installerPath = await _updater.DownloadInstallerAsync(result, ct: ct);
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
}

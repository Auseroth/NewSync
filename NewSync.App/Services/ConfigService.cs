using System.Text.Json;
using NewSync.App.Models;

namespace NewSync.App.Services;

public sealed class ConfigService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task<AppConfig> LoadAppConfigAsync(bool createIfMissing, CancellationToken ct = default)
    {
        AppPaths.EnsureDirectories();

        if (!File.Exists(AppPaths.AppConfigPath))
        {
            var defaults = GetDefaultAppConfig();
            if (createIfMissing)
            {
                await SaveAppConfigAsync(defaults, ct);
            }

            return defaults;
        }

        var json = await File.ReadAllTextAsync(AppPaths.AppConfigPath, ct);
        var config = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions) ?? GetDefaultAppConfig();
        return MergeDefaults(config);
    }

    public async Task<CalendarConfig> LoadCalendarConfigAsync(bool createIfMissing, CancellationToken ct = default)
    {
        AppPaths.EnsureDirectories();

        if (!File.Exists(AppPaths.CalConfigPath))
        {
            var defaults = GetDefaultCalendarConfig();
            if (createIfMissing)
            {
                await SaveCalendarConfigAsync(defaults, ct);
            }

            return defaults;
        }

        var json = await File.ReadAllTextAsync(AppPaths.CalConfigPath, ct);
        var config = JsonSerializer.Deserialize<CalendarConfig>(json, _jsonOptions) ?? GetDefaultCalendarConfig();
        return SanitizeCalendars(config);
    }

    public async Task SaveAppConfigAsync(AppConfig config, CancellationToken ct = default)
    {
        AppPaths.EnsureDirectories();
        var merged = MergeDefaults(config);
        var json = JsonSerializer.Serialize(merged, _jsonOptions);

        if (File.Exists(AppPaths.AppConfigPath))
        {
            File.Delete(AppPaths.AppConfigPath);
        }

        await File.WriteAllTextAsync(AppPaths.AppConfigPath, json, ct);
    }

    public async Task SaveCalendarConfigAsync(CalendarConfig config, CancellationToken ct = default)
    {
        AppPaths.EnsureDirectories();
        var sanitized = SanitizeCalendars(config);
        var json = JsonSerializer.Serialize(sanitized, _jsonOptions);

        if (File.Exists(AppPaths.CalConfigPath))
        {
            File.Delete(AppPaths.CalConfigPath);
        }

        await File.WriteAllTextAsync(AppPaths.CalConfigPath, json, ct);
    }

    public AppConfig GetDefaultAppConfig() => new()
    {
        Display = new DisplaySettings
        {
            BackgroundColor = "#000000",
            CalendarNameColor = "#888888",
            TimeEventColor = "#FFA500",
            BodyColor = "#FFFFFF",
            FontSize = 20
        },
        Updates = new UpdateSettings
        {
            GithubReleasesUrl = string.Empty,
            CheckOnStartup = true,
            AutoUpdateIntervalHours = 24
        }
    };

    public CalendarConfig GetDefaultCalendarConfig() => new()
    {
        Calendars =
        [
            new CalendarSource
            {
                Name = "All Station Calendar",
                Url = string.Empty,
                Stations = ["all"],
                DaysOut = 0,
                Permanent = true
            }
        ]
    };

    private static AppConfig MergeDefaults(AppConfig config)
    {
        var defaults = new ConfigService().GetDefaultAppConfig();

        config.Display ??= defaults.Display;
        config.Updates ??= defaults.Updates;

        config.Display.BackgroundColor = string.IsNullOrWhiteSpace(config.Display.BackgroundColor)
            ? defaults.Display.BackgroundColor
            : config.Display.BackgroundColor;
        config.Display.CalendarNameColor = string.IsNullOrWhiteSpace(config.Display.CalendarNameColor)
            ? defaults.Display.CalendarNameColor
            : config.Display.CalendarNameColor;
        config.Display.TimeEventColor = string.IsNullOrWhiteSpace(config.Display.TimeEventColor)
            ? defaults.Display.TimeEventColor
            : config.Display.TimeEventColor;
        config.Display.BodyColor = string.IsNullOrWhiteSpace(config.Display.BodyColor)
            ? defaults.Display.BodyColor
            : config.Display.BodyColor;
        if (config.Display.FontSize <= 0)
        {
            config.Display.FontSize = defaults.Display.FontSize;
        }

        if (config.Updates.AutoUpdateIntervalHours < 0)
        {
            config.Updates.AutoUpdateIntervalHours = defaults.Updates.AutoUpdateIntervalHours;
        }

        return config;
    }

    private CalendarConfig SanitizeCalendars(CalendarConfig config)
    {
        config.Calendars ??= [];

        foreach (var c in config.Calendars)
        {
            c.Name = string.IsNullOrWhiteSpace(c.Name) ? "Unnamed Calendar" : c.Name.Trim();
            c.Stations ??= [];
            c.Stations = c.Stations
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            c.DaysOut = Math.Max(0, c.DaysOut);
        }

        var allStation = config.Calendars.FirstOrDefault(c => c.Permanent || string.Equals(c.Name, "All Station Calendar", StringComparison.OrdinalIgnoreCase));
        if (allStation is null)
        {
            allStation = GetDefaultCalendarConfig().Calendars[0];
            config.Calendars.Insert(0, allStation);
        }

        allStation.Name = "All Station Calendar";
        allStation.Permanent = true;
        allStation.Stations = ["all"];

        return config;
    }
}

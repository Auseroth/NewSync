namespace NewSync.App.Models;

public sealed class AppConfig
{
    public DisplaySettings Display { get; set; } = new();
    public UpdateSettings Updates { get; set; } = new();
}

public sealed class DisplaySettings
{
    public string BackgroundColor { get; set; } = "#000000";
    public string CalendarNameColor { get; set; } = "#888888";
    public string TimeEventColor { get; set; } = "#FFA500";
    public string BodyColor { get; set; } = "#FFFFFF";
    public double FontSize { get; set; } = 20;
}

public sealed class UpdateSettings
{
    public string GithubReleasesUrl { get; set; } = string.Empty;
}
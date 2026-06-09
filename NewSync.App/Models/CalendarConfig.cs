namespace NewSync.App.Models;

public sealed class CalendarConfig
{
    public List<CalendarSource> Calendars { get; set; } = new();
}

public sealed class CalendarSource
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public List<string> Stations { get; set; } = new();
    public int DaysOut { get; set; }
    public bool Permanent { get; set; }
}

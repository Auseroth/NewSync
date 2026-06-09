using NewSync.App.Models;

namespace NewSync.App.Services;

public static class StationMatcher
{
    public static List<CalendarSource> GetMatches(CalendarConfig config, string machineName)
    {
        var matches = config.Calendars
            .Where(c => MatchesCalendar(c, machineName))
            .ToList();

        if (matches.Count > 0)
        {
            return matches;
        }

        var allStation = config.Calendars.FirstOrDefault(c => c.Permanent)
            ?? config.Calendars.FirstOrDefault(c => c.Stations.Any(s =>
                string.Equals(s, "all", StringComparison.OrdinalIgnoreCase)));

        return allStation is null ? [] : [allStation];
    }

    private static bool MatchesCalendar(CalendarSource source, string machineName)
    {
        if (source.Stations.Any(s => string.Equals(s, "all", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return source.Stations.Any(station =>
            machineName.Contains(station, StringComparison.OrdinalIgnoreCase));
    }
}

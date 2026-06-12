using NewSync.App.Models;

namespace NewSync.App.Services;

public static class StationMatcher
{
    private static readonly char[] _delimiters = ['-', '_', '.', ' '];

    public static List<CalendarSource> GetMatches(CalendarConfig config, string machineName)
    {
        var segments = machineName
            .Split(_delimiters, StringSplitOptions.RemoveEmptyEntries);

        var matches = config.Calendars
            .Where(c => MatchesCalendar(c, segments))
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

    private static bool MatchesCalendar(CalendarSource source, string[] machineSegments)
    {
        if (source.Stations.Any(s => string.Equals(s, "all", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return source.Stations.Any(station =>
            machineSegments.Any(seg => SegmentMatchesStation(seg, station)));
    }

    /// <summary>
    /// Matches a machine name segment against a station entry.
    /// Exact:     "S05A" station vs "S05A" segment → match
    /// S-prefix:  "05"   station vs "S05A" segment → match (S + code + optional letters only)
    /// No match:  "05"   station vs "05067" segment → no match (no S prefix, not exact)
    /// </summary>
    private static bool SegmentMatchesStation(string segment, string station)
    {
        if (string.Equals(segment, station, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // S-prefix rule: segment must be S + station code + optional trailing letters (A, B…)
        if (segment.Length > station.Length &&
            char.ToUpperInvariant(segment[0]) == 'S')
        {
            var afterS = segment[1..];
            if (afterS.StartsWith(station, StringComparison.OrdinalIgnoreCase))
            {
                var suffix = afterS[station.Length..];
                return suffix.Length == 0 || suffix.All(char.IsLetter);
            }
        }

        return false;
    }
}

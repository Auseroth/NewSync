using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net.Http;
using NewSync.App.Models;

namespace NewSync.App.Services;

public sealed class CalendarSyncService
{
    private readonly HttpClient _httpClient;
    private readonly LoggingService _logger;

    public CalendarSyncService(LoggingService logger)
    {
        _logger = logger;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("NewSync/1.0");
    }

    public async Task<IReadOnlyList<TickerEvent>> LoadCachedEventsAsync(CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(AppPaths.SelectedEventsPath))
            {
                return [];
            }

            var json = await File.ReadAllTextAsync(AppPaths.SelectedEventsPath, ct);
            var events = JsonSerializer.Deserialize<List<TickerEvent>>(json) ?? [];
            return events.OrderBy(e => e.StartLocal).ToList();
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to load cached events.", ex);
            return [];
        }
    }

    public async Task<IReadOnlyList<TickerEvent>> RefreshAsync(CalendarConfig config, string machineName, CancellationToken ct = default)
    {
        AppPaths.EnsureDirectories();
        var selectedCalendars = StationMatcher.GetMatches(config, machineName);
        var allEvents = new List<TickerEvent>();

        for (var i = 0; i < selectedCalendars.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var calendar = selectedCalendars[i];

            if (string.IsNullOrWhiteSpace(calendar.Url))
            {
                continue;
            }

            try
            {
                var ical = await _httpClient.GetStringAsync(calendar.Url, ct);
                var rawPath = Path.Combine(AppPaths.LocalDataDir, $"raw_{i}.ical");
                await File.WriteAllTextAsync(rawPath, ical, ct);

                var parsed = ParseIcal(ical, calendar.Name);
                var filtered = FilterByDaysOut(parsed, calendar.DaysOut);
                allEvents.AddRange(filtered);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to sync calendar '{calendar.Name}'.", ex);
            }
        }

        var ordered = allEvents
            .OrderBy(e => e.StartLocal)
            .ThenBy(e => e.CalendarName)
            .ToList();

        await WriteDisplayFilesAsync(ordered, ct);
        _logger.Info($"Calendar refresh complete. Events: {ordered.Count}.");
        return ordered;
    }

    private async Task WriteDisplayFilesAsync(List<TickerEvent> events, CancellationToken ct)
    {
        var text = BuildDisplayText(events);
        await File.WriteAllTextAsync(AppPaths.DisplayTodayPath, text, ct);

        var json = JsonSerializer.Serialize(events, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        await File.WriteAllTextAsync(AppPaths.SelectedEventsPath, json, ct);
    }

    private static string BuildDisplayText(IEnumerable<TickerEvent> events)
    {
        var sb = new StringBuilder();
        foreach (var e in events)
        {
            sb.AppendLine($"[{e.CalendarName}] {e.StartLocal:h:mm tt}-{e.EndLocal:h:mm tt} {e.Summary}");
            if (!string.IsNullOrWhiteSpace(e.Description))
            {
                sb.AppendLine($"  {e.Description}");
            }
        }

        return sb.ToString();
    }

    private static List<TickerEvent> FilterByDaysOut(List<TickerEvent> events, int daysOut)
    {
        var today = DateTime.Now.Date;
        var max = today.AddDays(daysOut);

        return events
            .Where(e => e.StartLocal.Date >= today && e.StartLocal.Date <= max)
            .ToList();
    }

    private static List<TickerEvent> ParseIcal(string content, string calendarName)
    {
        var results = new List<TickerEvent>();
        var lines = UnfoldLines(content);

        EventBuilder? current = null;
        foreach (var line in lines)
        {
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                current = new EventBuilder();
                continue;
            }

            if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null && current.IsValid())
                {
                    if (string.IsNullOrWhiteSpace(current.Status) ||
                        current.Status.Equals("CONFIRMED", StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new TickerEvent
                        {
                            CalendarName = calendarName,
                            StartLocal = current.Start!.Value,
                            EndLocal = current.End ?? current.Start.Value,
                            Summary = string.IsNullOrWhiteSpace(current.Summary) ? "(No Title)" : current.Summary,
                            Description = current.Description ?? string.Empty
                        });
                    }
                }

                current = null;
                continue;
            }

            if (current is null)
            {
                continue;
            }

            var idx = line.IndexOf(':');
            if (idx <= 0)
            {
                continue;
            }

            var rawKey = line[..idx];
            var value = line[(idx + 1)..].Trim();
            var key = rawKey.Split(';', 2)[0].Trim().ToUpperInvariant();

            switch (key)
            {
                case "DTSTART":
                    current.Start = ParseDate(value);
                    break;
                case "DTEND":
                    current.End = ParseDate(value);
                    break;
                case "SUMMARY":
                    current.Summary = value;
                    break;
                case "DESCRIPTION":
                    current.Description = StripHtml(UnescapeIcal(value));
                    break;
                case "STATUS":
                    current.Status = value;
                    break;
            }
        }

        return results;
    }

    private static List<string> UnfoldLines(string content)
    {
        var raw = content.Replace("\r", string.Empty).Split('\n');
        var output = new List<string>();

        foreach (var line in raw)
        {
            if (line.StartsWith(" ") || line.StartsWith("\t"))
            {
                if (output.Count > 0)
                {
                    output[^1] += line.TrimStart();
                }
            }
            else
            {
                output.Add(line);
            }
        }

        return output;
    }

    private static DateTime? ParseDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTime.TryParseExact(
            value,
            ["yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmm'Z'"],
            null,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var utc))
        {
            return utc.ToLocalTime();
        }

        if (DateTime.TryParseExact(
            value,
            ["yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm", "yyyyMMdd"],
            null,
            System.Globalization.DateTimeStyles.AssumeLocal,
            out var local))
        {
            return local;
        }

        return null;
    }

    // Unescape iCal backslash sequences before HTML processing.
    private static string UnescapeIcal(string value)
    {
        return value
            .Replace("\\n", Environment.NewLine)
            .Replace("\\N", Environment.NewLine)
            .Replace("\\,", ",")
            .Replace("\\;", ";")
            .Replace("\\\\", "\\");
    }

    // Strip HTML tags and decode HTML entities so descriptions display as plain text.
    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return html;
        }

        var text = WebUtility.HtmlDecode(html);

        // Preserve paragraph breaks and list structure when stripping markup.
        text = Regex.Replace(text, @"<\s*br\s*/?\s*>", Environment.NewLine, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<\s*(p|div|li|h\d)[^>]*>", string.Empty, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</\s*(p|div|li|h\d)\s*>", Environment.NewLine, RegexOptions.IgnoreCase);

        // Strip any remaining tags after block separators have been normalized.
        text = Regex.Replace(text, @"<[^>]+>", string.Empty);

        // Keep actual line breaks, but trim stray spaces around them.
        text = text.Replace("\r", string.Empty);
        text = Regex.Replace(text, @"[ \t]+\n", "\n");
        text = Regex.Replace(text, @"\n[ \t]+", "\n");
        text = Regex.Replace(text, @"\n{2,}", "\n");

        return text.Trim();
    }

    private sealed class EventBuilder
    {
        public DateTime? Start { get; set; }
        public DateTime? End { get; set; }
        public string? Summary { get; set; }
        public string? Description { get; set; }
        public string? Status { get; set; }

        public bool IsValid() => Start.HasValue;
    }
}

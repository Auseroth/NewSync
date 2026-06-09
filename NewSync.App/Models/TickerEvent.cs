namespace NewSync.App.Models;

public sealed class TickerEvent
{
    public string CalendarName { get; set; } = string.Empty;
    public DateTime StartLocal { get; set; }
    public DateTime EndLocal { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public string TimeSummary => $"{StartLocal:h:mm tt} - {EndLocal:h:mm tt}  ·  {Summary}";

    public string BodyLine
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Description))
            {
                return Description.Trim();
            }

            return Summary.Trim();
        }
    }
}

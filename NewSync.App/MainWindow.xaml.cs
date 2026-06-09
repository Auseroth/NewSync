using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using NewSync.App.Models;

namespace NewSync.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _cycleTimer;
    private readonly DispatcherTimer _bodyScrollTimer;
    private IReadOnlyList<TickerEvent> _events = [];
    private int _eventIndex = -1;
    private List<string> _bodySegments = [];
    private int _bodySegmentIndex;

    public event EventHandler? CloseProgramRequested;

    public MainWindow()
    {
        InitializeComponent();

        _cycleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(8)
        };
        _cycleTimer.Tick += (_, _) => ShowNextEvent();

        _bodyScrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _bodyScrollTimer.Tick += (_, _) => RotateBodySegment();
    }

    public void PlaceOnPrimaryScreen()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Left;
        Width = wa.Width;
        Height = 110;
        Top = wa.Bottom - Height;
    }

    public void ApplyDisplay(DisplaySettings settings)
    {
        Background = ParseBrush(settings.BackgroundColor, System.Windows.Media.Brushes.Black);
        CalendarNameText.Foreground = ParseBrush(settings.CalendarNameColor, System.Windows.Media.Brushes.Gray);
        TimeSummaryText.Foreground = ParseBrush(settings.TimeEventColor, System.Windows.Media.Brushes.Orange);
        BodyText.Foreground = ParseBrush(settings.BodyColor, System.Windows.Media.Brushes.White);

        CalendarNameText.FontSize = settings.FontSize;
        TimeSummaryText.FontSize = settings.FontSize + 2;
        BodyText.FontSize = settings.FontSize;
    }

    public void SetEvents(IReadOnlyList<TickerEvent> events)
    {
        _events = events;
        _eventIndex = -1;

        if (_events.Count == 0)
        {
            ShowStatus("No events found for this station.");
            return;
        }

        ShowNextEvent();
        _cycleTimer.Start();
    }

    public void ShowStatus(string message)
    {
        _cycleTimer.Stop();
        _bodyScrollTimer.Stop();
        CalendarNameText.Text = "NewSync";
        TimeSummaryText.Text = message;
        BodyText.Text = string.Empty;
    }

    private void ShowNextEvent()
    {
        if (_events.Count == 0)
        {
            return;
        }

        _eventIndex = (_eventIndex + 1) % _events.Count;
        var item = _events[_eventIndex];

        CalendarNameText.Text = item.CalendarName;
        TimeSummaryText.Text = item.TimeSummary;

        _bodySegments = SegmentText(item.BodyLine, 80);
        _bodySegmentIndex = 0;
        BodyText.Text = _bodySegments[0];

        if (_bodySegments.Count > 1)
        {
            _bodyScrollTimer.Start();
        }
        else
        {
            _bodyScrollTimer.Stop();
        }
    }

    private void RotateBodySegment()
    {
        if (_bodySegments.Count <= 1)
        {
            _bodyScrollTimer.Stop();
            return;
        }

        _bodySegmentIndex = (_bodySegmentIndex + 1) % _bodySegments.Count;
        BodyText.Text = _bodySegments[_bodySegmentIndex];
    }

    private static List<string> SegmentText(string text, int segmentLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [string.Empty];
        }

        var output = new List<string>();
        var remaining = text.Trim();

        while (remaining.Length > segmentLength)
        {
            var split = remaining.LastIndexOf(' ', segmentLength);
            if (split <= 0)
            {
                split = segmentLength;
            }

            output.Add(remaining[..split].Trim());
            remaining = remaining[split..].Trim();
        }

        output.Add(remaining);
        return output;
    }

    private static System.Windows.Media.Brush ParseBrush(string colorHex, System.Windows.Media.Brush fallback)
    {
        try
        {
            var converter = new BrushConverter();
            if (converter.ConvertFromString(colorHex) is System.Windows.Media.Brush b)
            {
                return b;
            }
        }
        catch
        {
            // ignored
        }

        return fallback;
    }

    private void CloseProgram_Click(object sender, RoutedEventArgs e)
    {
        CloseProgramRequested?.Invoke(this, EventArgs.Empty);
    }
}

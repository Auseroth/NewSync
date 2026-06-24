using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NewSync.App.Models;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using BrushConverter = System.Windows.Media.BrushConverter;

namespace NewSync.App;

public partial class MainWindow : Window
{
    // Fixed pixel height of the ticker bar — never changes for any reason.
    private const double FixedWindowHeight = 290.0;

    // Seconds to hold content at the end (scrolled or not) before advancing.
    private const double PostDisplayPauseSec = 5.0;

    // Pixels per second for continuous upward scroll.
    private const double ScrollPixelsPerSecond = 30.0;
    private const int ScrollTickMilliseconds = 16;

    private DispatcherTimer? _pauseTimer;
    private DispatcherTimer? _scrollTimer;
    private double _scrollTarget;
    private IReadOnlyList<TickerEvent> _events = [];
    private int _eventIndex = -1;

    // Incremented on every new display call; lets deferred callbacks detect stale invocations.
    private int _displayGeneration;

    private bool _isStatus;
    private string _statusMessage = string.Empty;
    private Brush _timeEventBrush = Brushes.Orange;
    private Brush _bodyBrush = Brushes.White;

    // Approximate header row height at current font size; used to compute viewport height.
    // FontSize(30) * 1.4 + 6 = 48 by default.
    private double _slotHeight = 48.0;

    private DisplaySettings _displaySettings = new();

    public event EventHandler? CloseProgramRequested;

    public MainWindow()
    {
        InitializeComponent();
    }

    public void PlaceOnPrimaryScreen(DisplaySettings settings)
    {
        _displaySettings = settings;
        _slotHeight = settings.FontSize * 1.4 + 6;
        ApplyFixedLayout();
        UpdatePosition();

        SystemParameters.StaticPropertyChanged -= OnWorkAreaChanged;
        SystemParameters.StaticPropertyChanged += OnWorkAreaChanged;
    }

    public void ApplyDisplay(DisplaySettings settings)
    {
        _displaySettings = settings;
        _slotHeight = settings.FontSize * 1.4 + 6;

        Background = ParseBrush(settings.BackgroundColor, Brushes.Black);
        CalendarNameText.Foreground = ParseBrush(settings.CalendarNameColor, Brushes.Gray);
        CalendarNameText.FontSize = settings.FontSize;
        _timeEventBrush = ParseBrush(settings.TimeEventColor, Brushes.Orange);
        _bodyBrush = ParseBrush(settings.BodyColor, Brushes.White);

        ApplyFixedLayout();
        StopAllTimers();

        if (_isStatus)
        {
            ShowStatus(_statusMessage);
            return;
        }

        if (_eventIndex >= 0 && _eventIndex < _events.Count)
        {
            DisplayEvent(_events[_eventIndex]);
        }
        else
        {
            UpdatePosition();
        }
    }

    public void SetEvents(IReadOnlyList<TickerEvent> events)
    {
        _events = events;
        _eventIndex = -1;
        StopAllTimers();

        if (_events.Count == 0)
        {
            ShowStatus("No events found for this station.");
            return;
        }

        ShowNextEvent();
    }

    public void ShowStatus(string message)
    {
        StopAllTimers();
        _isStatus = true;
        _statusMessage = message;
        _displayGeneration++;
        CalendarNameText.Text = "NewSync";

        ClearScrollContent();
        ScrollContent.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = _displaySettings.FontSize,
            Foreground = _bodyBrush,
            TextWrapping = TextWrapping.Wrap
        });
    }

    private void ShowNextEvent()
    {
        if (_events.Count == 0) return;

        _eventIndex = (_eventIndex + 1) % _events.Count;
        DisplayEvent(_events[_eventIndex]);
    }

    private void DisplayEvent(TickerEvent item)
    {
        StopAllTimers();
        _isStatus = false;
        var generation = ++_displayGeneration;

        // 1. Set the top calendar name
        CalendarNameText.Text = item.CalendarName;

        // 2. Pin the event summary to our new non-scrolling TextBlock
        EventTitleText.Text = item.TimeSummary;
        EventTitleText.Foreground = _timeEventBrush; // Keeps your orange accent color

        ClearScrollContent();

        // 3. ONLY add the description to the scroll container
        if (!string.IsNullOrWhiteSpace(item.Description))
        {
            AddDescriptionContent(item.Description.Trim());
        }

        // Defer the scroll decision
        Dispatcher.InvokeAsync(() =>
        {
            if (generation != _displayGeneration) return;
            CheckAndStartScroll();
        }, DispatcherPriority.ContextIdle);
    }

    // Compares actual rendered content height to viewport; scrolls or pauses accordingly.
    private void CheckAndStartScroll()
    {
        ScrollViewport.UpdateLayout();
        ScrollContent.UpdateLayout();

        // If layout is not fully ready yet, try again on the next UI tick.
        if (ScrollViewport.ActualHeight <= 0 || ScrollViewport.ActualWidth <= 0)
        {
            Dispatcher.InvokeAsync(CheckAndStartScroll, DispatcherPriority.ContextIdle);
            return;
        }

        var contentHeight = ScrollViewport.ExtentHeight;
        var viewportHeight = ScrollViewport.ViewportHeight;
        var overflow = contentHeight - viewportHeight;

        if (overflow > 2)
        {
            StartScrollAnimation(overflow);
        }
        else
        {
            ScheduleNextEvent();
        }
    }

    private void StartScrollAnimation(double totalScroll)
    {
        _scrollTimer?.Stop();
        ScrollViewport.ScrollToVerticalOffset(0);
        _scrollTarget = totalScroll;

        _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ScrollTickMilliseconds) };
        _scrollTimer.Tick += (_, _) =>
        {
            var current = ScrollViewport.VerticalOffset;
            var step = ScrollPixelsPerSecond * (ScrollTickMilliseconds / 1000.0);
            var next = Math.Min(_scrollTarget, current + step);

            ScrollViewport.ScrollToVerticalOffset(next);

            if (next >= _scrollTarget - 0.5)
            {
                _scrollTimer?.Stop();
                _scrollTimer = null;
                ScheduleNextEvent();
            }
        };
        _scrollTimer.Start();
    }

    private void ScheduleNextEvent()
    {
        _pauseTimer?.Stop();
        _pauseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(PostDisplayPauseSec) };
        _pauseTimer.Tick += (_, _) =>
        {
            _pauseTimer?.Stop();
            _pauseTimer = null;
            ShowNextEvent();
        };
        _pauseTimer.Start();
    }

    private void AddDescriptionContent(string description)
    {
        var lines = description.Replace("\r", string.Empty).Split('\n');
        var paragraphSeen = false;
        var sb = new StringBuilder();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!paragraphSeen)
            {
                sb.AppendLine(line);
                sb.AppendLine();
                paragraphSeen = true;
                continue;
            }

            if (line.EndsWith(":", StringComparison.Ordinal))
            {
                sb.AppendLine(line);
                continue;
            }

            sb.AppendLine($"• {line}");
        }

        var text = sb.ToString().TrimEnd();
        if (text.Length == 0)
        {
            return;
        }

        ScrollContent.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = _displaySettings.FontSize,
            Foreground = _bodyBrush,
            TextWrapping = TextWrapping.Wrap
        });
    }

    private void StopAllTimers()
    {
        _pauseTimer?.Stop();
        _pauseTimer = null;

        _scrollTimer?.Stop();
        _scrollTimer = null;
    }

    private void ClearScrollContent()
    {
        ScrollViewport.ScrollToVerticalOffset(0);
        ScrollContent.Children.Clear();
    }

    // Window height is always FixedWindowHeight.
    // Viewport height = fixed height minus the header row and Border padding.
    private void ApplyFixedLayout()
    {
        // Keep the overall window height calculation intact
        Height = FixedWindowHeight;

        ScrollViewport.ClearValue(HeightProperty);
    }

    private void UpdatePosition()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Left;
        Width = wa.Width;
        Top = wa.Bottom - Height;
    }

    private void OnWorkAreaChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.WorkArea))
        {
            UpdatePosition();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        StopAllTimers();
        SystemParameters.StaticPropertyChanged -= OnWorkAreaChanged;
        base.OnClosed(e);
    }

    private static Brush ParseBrush(string colorHex, Brush fallback)
    {
        try
        {
            var converter = new BrushConverter();
            if (converter.ConvertFromString(colorHex) is Brush b)
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

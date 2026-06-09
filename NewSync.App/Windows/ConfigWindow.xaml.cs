using System.Collections.ObjectModel;
using System.Windows;
using NewSync.App.Models;
using NewSync.App.Services;

namespace NewSync.App.Windows;

public partial class ConfigWindow : Window
{
    public sealed class CalendarRow
    {
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string StationsText { get; set; } = string.Empty;
        public int DaysOut { get; set; }
        public bool Permanent { get; set; }
    }

    private readonly ConfigService _configService;
    private readonly UpdateService _updateService;

    public ObservableCollection<CalendarRow> CalendarRows { get; } = [];

    public event EventHandler? SaveRunRequested;
    public event EventHandler? SaveExitRequested;
    public event EventHandler? CloseProgramRequested;

    public ConfigWindow(ConfigService configService, UpdateService updateService)
    {
        InitializeComponent();
        _configService = configService;
        _updateService = updateService;

        CalendarsGrid.ItemsSource = CalendarRows;
    }

    public void Load(AppConfig appConfig, CalendarConfig calendarConfig)
    {
        CalendarRows.Clear();
        foreach (var c in calendarConfig.Calendars)
        {
            CalendarRows.Add(new CalendarRow
            {
                Name = c.Name,
                Url = c.Url,
                StationsText = string.Join(",", c.Stations),
                DaysOut = c.DaysOut,
                Permanent = c.Permanent
            });
        }

        BackgroundColorBox.Text = appConfig.Display.BackgroundColor;
        CalendarNameColorBox.Text = appConfig.Display.CalendarNameColor;
        TimeEventColorBox.Text = appConfig.Display.TimeEventColor;
        BodyColorBox.Text = appConfig.Display.BodyColor;
        FontSizeBox.Text = appConfig.Display.FontSize.ToString("0");

        GithubUrlBox.Text = appConfig.Updates.GithubReleasesUrl;
        CheckOnStartupBox.IsChecked = appConfig.Updates.CheckOnStartup;
        AutoCheckHoursBox.Text = appConfig.Updates.AutoUpdateIntervalHours.ToString();
        StatusText.Text = "";
    }

    public (AppConfig appConfig, CalendarConfig calConfig) BuildConfigs()
    {
        var appConfig = new AppConfig
        {
            Display = new DisplaySettings
            {
                BackgroundColor = BackgroundColorBox.Text.Trim(),
                CalendarNameColor = CalendarNameColorBox.Text.Trim(),
                TimeEventColor = TimeEventColorBox.Text.Trim(),
                BodyColor = BodyColorBox.Text.Trim(),
                FontSize = ParseDouble(FontSizeBox.Text, 20)
            },
            Updates = new UpdateSettings
            {
                GithubReleasesUrl = GithubUrlBox.Text.Trim(),
                CheckOnStartup = CheckOnStartupBox.IsChecked == true,
                AutoUpdateIntervalHours = ParseInt(AutoCheckHoursBox.Text, 24)
            }
        };

        var calConfig = new CalendarConfig
        {
            Calendars = CalendarRows.Select(r => new CalendarSource
            {
                Name = r.Name.Trim(),
                Url = r.Url.Trim(),
                Stations = r.StationsText
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList(),
                DaysOut = Math.Max(0, r.DaysOut),
                Permanent = r.Permanent
            }).ToList()
        };

        return (appConfig, calConfig);
    }

    private async void SaveRun_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (app, cal) = BuildConfigs();
            await _configService.SaveAppConfigAsync(app);
            await _configService.SaveCalendarConfigAsync(cal);
            SaveRunRequested?.Invoke(this, EventArgs.Empty);
            StatusText.Text = "Saved. Applying and running.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Save failed: {ex.Message}";
        }
    }

    private async void SaveExit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (app, cal) = BuildConfigs();
            await _configService.SaveAppConfigAsync(app);
            await _configService.SaveCalendarConfigAsync(cal);
            SaveExitRequested?.Invoke(this, EventArgs.Empty);
            StatusText.Text = "Saved. Exiting.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Save failed: {ex.Message}";
        }
    }

    private void CloseProgram_Click(object sender, RoutedEventArgs e)
    {
        CloseProgramRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        var (app, _) = BuildConfigs();
        _updateService.Configure(app);
        var result = await _updateService.CheckNowAsync();

        if (result?.UpdateAvailable == true)
        {
            StatusText.Text = $"Update available: {result.TagName}";
        }
        else
        {
            StatusText.Text = "No update available.";
        }
    }

    private void AddCalendar_Click(object sender, RoutedEventArgs e)
    {
        CalendarRows.Add(new CalendarRow
        {
            Name = "New Calendar",
            Url = string.Empty,
            StationsText = "",
            DaysOut = 0,
            Permanent = false
        });
    }

    private void DeleteCalendar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CalendarRow row })
        {
            return;
        }

        if (row.Permanent)
        {
            StatusText.Text = "All Station Calendar cannot be deleted.";
            return;
        }

        CalendarRows.Remove(row);
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        var help = "Google Calendar iCal setup:\n" +
                   "1) Open calendar settings.\n" +
                   "2) Find Secret address in iCal format.\n" +
                   "3) Paste URL in the calendar row URL field.\n\n" +
                   $"Config files:\n{AppPaths.AppConfigPath}\n{AppPaths.CalConfigPath}";

        System.Windows.MessageBox.Show(this, help, "NewSync Help", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static int ParseInt(string value, int fallback)
    {
        return int.TryParse(value, out var i) ? i : fallback;
    }

    private static double ParseDouble(string value, double fallback)
    {
        return double.TryParse(value, out var d) ? d : fallback;
    }
}

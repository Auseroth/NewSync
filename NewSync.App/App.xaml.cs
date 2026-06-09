using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Interop;
using NewSync.App.Models;
using NewSync.App.Services;
using NewSync.App.Windows;
using Forms = System.Windows.Forms;

namespace NewSync.App;

public partial class App : System.Windows.Application
{
    private const int HotkeyId = 4501;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VkN = 0x4E;
    private const int WmHotkey = 0x0312;

    private readonly LoggingService _logger = new();
    private readonly ConfigService _configService = new();

    private CalendarSyncService? _syncService;
    private UpdateService? _updateService;

    private MainWindow? _tickerWindow;
    private ConfigWindow? _configWindow;
    private HwndSource? _hotkeySource;
    private Forms.NotifyIcon? _tray;

    private AppConfig _appConfig = new();
    private CalendarConfig _calConfig = new();

    private CancellationTokenSource? _syncCts;
    private bool _configOnlyMode;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            AppPaths.EnsureDirectories();
            _syncService = new CalendarSyncService(_logger);
            _updateService = new UpdateService(_logger);

            _configOnlyMode = e.Args.Any(a => string.Equals(a, "/n", StringComparison.OrdinalIgnoreCase));
            if (_configOnlyMode && !IsAdmin())
            {
                RelaunchAsAdmin();
                Shutdown();
                return;
            }

            var appExisted = File.Exists(AppPaths.AppConfigPath);
            var calExisted = File.Exists(AppPaths.CalConfigPath);

            _appConfig = await _configService.LoadAppConfigAsync(createIfMissing: true);
            _calConfig = await _configService.LoadCalendarConfigAsync(createIfMissing: true);

            _updateService.Configure(_appConfig);
            _updateService.UpdateAvailable += (_, result) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (_configWindow is not null)
                    {
                        _configWindow.Title = $"NewSync Configuration - Update {result.TagName} available";
                    }
                });
            };

            if (_appConfig.Updates.CheckOnStartup)
            {
                _ = _updateService.CheckNowAsync();
            }

            _updateService.StartAutoChecks();

            if (_configOnlyMode)
            {
                OpenConfigWindow();
                return;
            }

            InitializeTickerWindow();
            InitializeTrayIcon();
            RegisterGlobalHotkey();

            var cached = await _syncService.LoadCachedEventsAsync();
            if (cached.Count > 0)
            {
                _tickerWindow?.SetEvents(cached);
            }
            else
            {
                _tickerWindow?.ShowStatus("Syncing calendar events...");
            }

            StartSyncLoop();

            if (!appExisted || !calExisted)
            {
                OpenConfigWindow();
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Startup failure", ex);
            System.Windows.MessageBox.Show("Startup failed. Check error.log for details.", "NewSync", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void InitializeTickerWindow()
    {
        _tickerWindow = new MainWindow();
        _tickerWindow.PlaceOnPrimaryScreen();
        _tickerWindow.ApplyDisplay(_appConfig.Display);
        _tickerWindow.CloseProgramRequested += (_, _) => Shutdown();
        _tickerWindow.SourceInitialized += (_, _) => AttachHotkeyHook();
        _tickerWindow.Show();
    }

    private void OpenConfigWindow()
    {
        if (_configWindow is not null)
        {
            _configWindow.Activate();
            return;
        }

        _configWindow = new ConfigWindow(_configService, _updateService!);
        _configWindow.Load(_appConfig, _calConfig);

        _configWindow.SaveRunRequested += async (_, _) =>
        {
            _appConfig = await _configService.LoadAppConfigAsync(createIfMissing: true);
            _calConfig = await _configService.LoadCalendarConfigAsync(createIfMissing: true);
            _updateService!.Configure(_appConfig);

            _tickerWindow?.ApplyDisplay(_appConfig.Display);
            if (_configOnlyMode)
            {
                _configWindow?.Close();
                _configWindow = null;
                return;
            }

            await ForceRefreshNowAsync();
        };

        _configWindow.SaveExitRequested += (_, _) => Shutdown();
        _configWindow.CloseProgramRequested += (_, _) => Shutdown();
        _configWindow.Closed += (_, _) => _configWindow = null;
        _configWindow.Show();
    }

    private void StartSyncLoop()
    {
        _syncCts?.Cancel();
        _syncCts = new CancellationTokenSource();

        _ = Task.Run(async () =>
        {
            await ForceRefreshNowAsync();

            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
            while (await timer.WaitForNextTickAsync(_syncCts.Token))
            {
                await ForceRefreshNowAsync();
            }
        }, _syncCts.Token);
    }

    private async Task ForceRefreshNowAsync()
    {
        if (_syncService is null)
        {
            return;
        }

        try
        {
            Dispatcher.Invoke(() => _tickerWindow?.ShowStatus("Syncing calendar events..."));
            var events = await _syncService.RefreshAsync(_calConfig, Environment.MachineName, _syncCts?.Token ?? CancellationToken.None);
            Dispatcher.Invoke(() => _tickerWindow?.SetEvents(events));
        }
        catch (OperationCanceledException)
        {
            // normal during shutdown
        }
        catch (Exception ex)
        {
            _logger.Error("Refresh loop error", ex);
            Dispatcher.Invoke(() => _tickerWindow?.ShowStatus("Sync failed. Using cached events."));
        }
    }

    private void InitializeTrayIcon()
    {
        try
        {
            _tray = new Forms.NotifyIcon
            {
                Text = "NewSync",
                Visible = true,
                Icon = System.Drawing.SystemIcons.Application
            };

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open Config", null, (_, _) => Dispatcher.Invoke(OpenConfigWindow));
            menu.Items.Add("Close Program", null, (_, _) => Dispatcher.Invoke(Shutdown));
            _tray.ContextMenuStrip = menu;
        }
        catch (Exception ex)
        {
            _logger.Error("Tray icon initialization failed; continuing without tray.", ex);
        }
    }

    private void AttachHotkeyHook()
    {
        if (_tickerWindow is null)
        {
            return;
        }

        var helper = new WindowInteropHelper(_tickerWindow);
        _hotkeySource = HwndSource.FromHwnd(helper.Handle);
        _hotkeySource?.AddHook(WndProc);
    }

    private void RegisterGlobalHotkey()
    {
        if (_tickerWindow is null)
        {
            return;
        }

        var handle = new WindowInteropHelper(_tickerWindow).Handle;
        _ = RegisterHotKey(handle, HotkeyId, ModControl | ModShift | ModAlt, VkN);
    }

    private void UnregisterGlobalHotkey()
    {
        if (_tickerWindow is null)
        {
            return;
        }

        var handle = new WindowInteropHelper(_tickerWindow).Handle;
        _ = UnregisterHotKey(handle, HotkeyId);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            OpenConfigWindow();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static bool IsAdmin()
    {
        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void RelaunchAsAdmin()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = "/n",
            Verb = "runas",
            UseShellExecute = true
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _syncCts?.Cancel();
        UnregisterGlobalHotkey();
        _hotkeySource?.RemoveHook(WndProc);

        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }

        _updateService?.Dispose();
        base.OnExit(e);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

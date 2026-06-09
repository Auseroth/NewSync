namespace NewSync.App.Services;

public static class AppPaths
{
    public static string ProgramDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NewSync");

    public static string LocalDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NewSync");

    public static string AppConfigPath => Path.Combine(ProgramDataDir, "app_config.json");
    public static string CalConfigPath => Path.Combine(ProgramDataDir, "cal_config.json");

    public static string AppLogPath => Path.Combine(LocalDataDir, "app.log");
    public static string ErrorLogPath => Path.Combine(LocalDataDir, "error.log");
    public static string DisplayTodayPath => Path.Combine(LocalDataDir, "display_today.txt");
    public static string SelectedEventsPath => Path.Combine(LocalDataDir, "selected_events.json");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(ProgramDataDir);
        Directory.CreateDirectory(LocalDataDir);
    }
}

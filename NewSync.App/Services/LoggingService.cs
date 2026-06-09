namespace NewSync.App.Services;

public sealed class LoggingService
{
    private readonly object _gate = new();

    public void Info(string message)
    {
        Write(AppPaths.AppLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] INFO  {message}");
    }

    public void Error(string message, Exception? ex = null)
    {
        var full = ex is null ? message : $"{message}{Environment.NewLine}{ex}";
        Write(AppPaths.ErrorLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR {full}");
    }

    private void Write(string path, string line)
    {
        lock (_gate)
        {
            AppPaths.EnsureDirectories();
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }
}

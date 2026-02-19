using System.IO;

namespace Relay.Services;

public sealed class LoggingService
{
    private static readonly object Sync = new();
    private static readonly string BaseDirectory = AppContext.BaseDirectory;
    private static string LogsRoot => Path.Combine(BaseDirectory, "Logs");

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    public string GetLogsRoot()
    {
        Directory.CreateDirectory(LogsRoot);
        return LogsRoot;
    }

    public string GetLatestLogPath()
    {
        Directory.CreateDirectory(LogsRoot);
        var latest = Directory.EnumerateFiles(LogsRoot, "relay_*.log", SearchOption.TopDirectoryOnly)
            .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return latest ?? string.Empty;
    }

    private static void Write(string level, string message)
    {
        Directory.CreateDirectory(LogsRoot);
        var fileName = $"relay_{DateTime.Now:yyyyMMdd}.log";
        var path = Path.Combine(LogsRoot, fileName);
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";

        lock (Sync)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }
}

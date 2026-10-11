using System.Globalization;

namespace ThermalKun.App;

// Best-effort, per-process logs: logging must never prevent the panel from opening.
internal static class StartupLog
{
    private static readonly object Gate = new();
    internal static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ThermalKun", "logs",
        $"startup-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Environment.ProcessId}.log");

    internal static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath,
                    $"{DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)} " +
                    $"[thread {Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }

    internal static void Error(string stage, Exception exception) => Write($"{stage}: {exception}");
}

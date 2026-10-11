namespace ThermalKun.Core;

public enum MeasurementState { Available, Unsupported, Unavailable }

public sealed record Reading(double? Value, MeasurementState State, string Source, string Detail)
{
    public bool IsAvailable => State == MeasurementState.Available && Value is double v && double.IsFinite(v);
    public static Reading Available(double value, string source, string detail = "") =>
        double.IsFinite(value) ? new(value, MeasurementState.Available, source, detail) : Unavailable(source, "Non-finite reading rejected.");
    public static Reading Unsupported(string source, string detail) => new(null, MeasurementState.Unsupported, source, detail);
    public static Reading Unavailable(string source, string detail) => new(null, MeasurementState.Unavailable, source, detail);
    public string MissingLabel => State == MeasurementState.Unsupported ? "Unsupported" : "Unavailable";
}

public sealed record Snapshot(DateTimeOffset SampledAt, Reading CpuTemperature, Reading CpuLoad, Reading GpuLoad,
    Reading MemoryUsedGiB, Reading MemoryTotalGiB, Reading ThermalStatus, Reading MemoryPressure)
{
    public static Snapshot Empty(DateTimeOffset now) => new(now,
        Reading.Unsupported("Temperature", "Enable the optional local LibreHardwareMonitor loopback HTTP source in Settings. No driver is installed by this app."),
        Reading.Unavailable("GetSystemTimes", "Waiting for two counter samples."),
        Reading.Unavailable("PDH GPU Engine", "Waiting for two counter samples."),
        Reading.Unavailable("GlobalMemoryStatusEx", "Waiting for a sample."),
        Reading.Unavailable("GlobalMemoryStatusEx", "Waiting for a sample."),
        Reading.Unsupported("Windows", "No equivalent to macOS ProcessInfo.thermalState is implemented. Temperature alone does not prove throttling."),
        Reading.Unsupported("Windows", "No equivalent to the macOS kernel pressure state is implemented; memory percentage is not a pressure state."));
    public bool IsStale(DateTimeOffset now, int interval) => now - SampledAt > TimeSpan.FromSeconds(Math.Max(6, interval * 3));
}

public readonly record struct HistoryPoint(DateTimeOffset Timestamp, double? Temperature);
public sealed class TemperatureHistory
{
    public const int Capacity = 180;
    private readonly List<HistoryPoint> points = new();
    public IReadOnlyList<HistoryPoint> Points => points;
    public double? Peak { get; private set; }
    public void Add(Snapshot sample)
    {
        var value = sample.CpuTemperature.IsAvailable ? sample.CpuTemperature.Value : null;
        points.Add(new(sample.SampledAt, value));
        if (points.Count > Capacity) points.RemoveRange(0, points.Count - Capacity);
        if (value is double v) Peak = Peak is double p ? Math.Max(p, v) : v;
    }
    public void ResetPeak() => Peak = null;
    public void MarkGap(DateTimeOffset time) => Add(Snapshot.Empty(time));
}

public static class SensorMath
{
    public static double? CpuPercent(ulong previousIdle, ulong previousKernel, ulong previousUser,
        ulong idle, ulong kernel, ulong user)
    {
        if (idle < previousIdle || kernel < previousKernel || user < previousUser) return null;
        var idleDelta = (double)(idle - previousIdle);
        var total = (double)(kernel - previousKernel) + (user - previousUser);
        if (total <= 0 || idleDelta > total) return null;
        return Math.Clamp((total - idleDelta) / total * 100, 0, 100);
    }
    // Per-process counters of the same GPU engine are added; the busiest engine is shown.
    public static double? GpuPercent(IEnumerable<(string Instance, double Value)> counters)
    {
        var engines = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (instance, value) in counters)
        {
            if (!double.IsFinite(value) || value < 0 || value > 100) continue;
            var keyIndex = instance.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
            if (keyIndex < 0) continue;
            var key = instance[keyIndex..];
            engines[key] = engines.GetValueOrDefault(key) + value;
        }
        return engines.Count == 0 ? null : Math.Clamp(engines.Values.Max(), 0, 100);
    }
}

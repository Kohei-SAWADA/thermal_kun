using ThermalKun.Core;

namespace ThermalKun.App;

internal sealed record CpuTemperatureFrame(DateTimeOffset Timestamp, double? Celsius, string SensorId, string SensorName, string Detail);
internal static class TemperatureSelection
{
    internal static int Rank(string name) => name.Equals("CPU Package", StringComparison.OrdinalIgnoreCase) ? 0
        : name.Equals("Core (Tdie)", StringComparison.OrdinalIgnoreCase) || name.Equals("CPU (Tdie)", StringComparison.OrdinalIgnoreCase) ? 1
        : name.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase) ? 2 : int.MaxValue;

    internal static Reading ValidateFrame(CpuTemperatureFrame frame, DateTimeOffset now)
    {
        const string source = "LibreHardwareMonitor 0.9.6 · bundled CPU reader";
        var age = now - frame.Timestamp;
        if (age > TimeSpan.FromSeconds(6) || age < TimeSpan.FromSeconds(-2))
            return Reading.Unavailable(source, "温度の取得時刻が古い、または不正です。");
        if (frame.Celsius is not double value)
            return Reading.Unavailable(source, frame.Detail);
        var parts = frame.SensorId.Split('/');
        if (parts.Length != 5 || parts[0] != "" || !(parts[1] == "intelcpu" || parts[1] == "amdcpu") ||
            !uint.TryParse(parts[2], out _) || parts[3] != "temperature" || !uint.TryParse(parts[4], out _) ||
            Rank(frame.SensorName) == int.MaxValue || !double.IsFinite(value) || value is < -20 or > 150)
            return Reading.Unavailable(source, "CPU温度センサーの識別子、ラベル、測定範囲を確認できません。");
        return Reading.Available(value, source, $"{frame.SensorName} ({frame.SensorId}); acquisition {frame.Timestamp:O}. {frame.Detail}");
    }
}

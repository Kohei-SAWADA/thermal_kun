using ThermalKun.App;

internal static class TemperatureChecks
{
    internal static void Run()
    {
        var now = DateTimeOffset.UtcNow;
        var frame = new CpuTemperatureFrame(now, 52.5, "/intelcpu/0/temperature/0", "CPU Package", "test only");
        void Check(bool value) { if (!value) throw new Exception("Bundled CPU temperature boundary check failed."); }
        Check(TemperatureSelection.ValidateFrame(frame, now).Value == 52.5);
        Check(!TemperatureSelection.ValidateFrame(frame with { Timestamp = now.AddSeconds(-7) }, now).IsAvailable);
        Check(!TemperatureSelection.ValidateFrame(frame with { Timestamp = now.AddSeconds(3) }, now).IsAvailable);
        Check(!TemperatureSelection.ValidateFrame(frame with { Celsius = double.NaN }, now).IsAvailable);
        Check(!TemperatureSelection.ValidateFrame(frame with { Celsius = 151 }, now).IsAvailable);
        Check(!TemperatureSelection.ValidateFrame(frame with { Celsius = -21 }, now).IsAvailable);
        Check(!TemperatureSelection.ValidateFrame(frame with { SensorId = "/gpu-intel/0/temperature/0" }, now).IsAvailable);
        Check(!TemperatureSelection.ValidateFrame(frame with { SensorId = "/intelcpu/0/load/0" }, now).IsAvailable);
        Check(!TemperatureSelection.ValidateFrame(frame with { SensorName = "Motherboard" }, now).IsAvailable);
        Check(TemperatureSelection.ValidateFrame(frame with { Celsius = null, Detail = "permission required" }, now).Detail == "permission required");
        Check(TemperatureSelection.Rank("CPU Package") < TemperatureSelection.Rank("CPU Core #1"));
        Check(TemperatureSelection.ValidateFrame(frame with { SensorId = "/amdcpu/0/temperature/1", SensorName = "Core (Tdie)" }, now).IsAvailable);
        Console.WriteLine("PASS: 12 bundled CPU temperature checks (age, identity, ranges, missing values, priority). Synthetic data never enters the app UI.");
    }
}

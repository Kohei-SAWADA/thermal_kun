using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ThermalKun.App;
using ThermalKun.Core;

internal static class Program
{
    private static readonly List<string> Failures = new();
    private static readonly DateTimeOffset Epoch = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static int checks;

    private static int Main()
    {
        try
        {
            CheckReadings();
            CheckCpuCounters();
            CheckGpuCounters();
            CheckHistory();
            CheckStaleness();
            CheckPreferences();
            CheckTemperatureDiscovery();
            CheckTemperatureSensorGet();
            CheckTemperatureTransport();
            TemperatureChecks.Run();
            SensorPipeChecks.Run();
        }
        catch (Exception exception)
        {
            Failures.Add($"Unexpected check failure: {exception}");
        }

        foreach (var failure in Failures) Console.Error.WriteLine($"FAIL: {failure}");
        if (Failures.Count > 0)
        {
            Console.Error.WriteLine($"{Failures.Count} failed checks out of {checks} checks.");
            return 1;
        }
        Console.WriteLine($"PASS: {checks} cross-platform checks (readings, CPU/GPU aggregation, history, stale state, isolated preferences, local temperature parser and fake HTTP transport).");
        Console.WriteLine("These checks do not validate Windows sensors, native UI, drivers, startup registration, or a physical login cycle.");
        return 0;
    }

    private static void Expect(bool condition, string description)
    {
        checks++;
        if (!condition) Failures.Add(description);
    }

    private static void Near(double? actual, double expected, string description) =>
        Expect(actual is double value && double.IsFinite(value) && Math.Abs(value - expected) < 0.000_001, description);

    private static Snapshot Sample(int offset, Reading temperature) => Snapshot.Empty(Epoch.AddSeconds(offset)) with
    {
        CpuTemperature = temperature
    };

    private static void CheckReadings()
    {
        var missing = new[]
        {
            Reading.Unsupported("test", "No supported sensor."),
            Reading.Unavailable("test", "Access failed."),
            new Reading(null, MeasurementState.Available, "test", "Missing payload."),
            new Reading(0, MeasurementState.Unsupported, "test", "An unsupported payload is not a measured zero."),
            new Reading(0, MeasurementState.Unavailable, "test", "A failed payload is not a measured zero.")
        };
        foreach (var reading in missing)
            Expect(!reading.IsAvailable, $"Missing/unsupported reading must be unavailable: {reading.Detail}");

        Expect(missing[0].Value is null && missing[0].MissingLabel == "Unsupported", "Unsupported readings must retain a distinct missing label and no numeric value.");
        Expect(missing[1].Value is null && missing[1].MissingLabel == "Unavailable", "Temporary errors must retain their missing label and no numeric value.");

        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var rejected = Reading.Available(value, "test");
            Expect(!rejected.IsAvailable && rejected.Value is null && rejected.State == MeasurementState.Unavailable,
                $"Non-finite factory reading must be rejected: {value}");
            var raw = new Reading(value, MeasurementState.Available, "test", "Corrupt measurement.");
            Expect(!raw.IsAvailable, $"Non-finite raw reading must not become available: {value}");
        }
        Near(Reading.Available(0, "measured").Value, 0, "A real measured zero remains a valid value.");
        Expect(Reading.Available(0, "measured").IsAvailable, "Measured zero must be distinguishable from unavailable data.");

        var empty = Snapshot.Empty(Epoch);
        foreach (var reading in new[] { empty.CpuTemperature, empty.CpuLoad, empty.GpuLoad, empty.MemoryUsedGiB,
                     empty.MemoryTotalGiB, empty.ThermalStatus, empty.MemoryPressure })
            Expect(!reading.IsAvailable && reading.Value is null, "Initial state must not fabricate a numeric value or normal state.");
        Expect(empty.ThermalStatus.State == MeasurementState.Unsupported && empty.MemoryPressure.State == MeasurementState.Unsupported,
            "Windows thermal state and memory pressure must remain explicitly unsupported when no corresponding source exists.");
        Expect(empty.ThermalStatus.Source == "Windows" && !string.IsNullOrWhiteSpace(empty.ThermalStatus.Detail),
            "Unsupported OS thermal state must explain its source and limitation.");
    }

    private static void CheckCpuCounters()
    {
        // Windows kernel time already includes idle time. Subtract idle once, rather than adding it to total.
        Near(SensorMath.CpuPercent(100, 200, 100, 120, 260, 140), 80,
            "CPU busy percentage must use delta(kernel + user - idle) / delta(kernel + user).");
        Near(SensorMath.CpuPercent(100, 200, 100, 160, 260, 100), 0, "An entirely idle interval must produce a measured zero.");
        Near(SensorMath.CpuPercent(100, 200, 100, 100, 260, 140), 100, "An interval without idle ticks must produce 100% busy.");
        Expect(SensorMath.CpuPercent(100, 200, 100, 100, 200, 100) is null, "An unchanged counter interval must not fabricate 0%.");
        Expect(SensorMath.CpuPercent(100, 200, 100, 99, 260, 140) is null, "Idle counter regression must invalidate the interval.");
        Expect(SensorMath.CpuPercent(100, 200, 100, 120, 199, 140) is null, "Kernel counter regression must invalidate the interval.");
        Expect(SensorMath.CpuPercent(100, 200, 100, 120, 260, 99) is null, "User counter regression must invalidate the interval.");
        Expect(SensorMath.CpuPercent(100, 200, 100, 250, 260, 140) is null, "Idle ticks exceeding total ticks must invalidate the interval.");
        Near(SensorMath.CpuPercent(ulong.MaxValue - 100, ulong.MaxValue - 100, ulong.MaxValue - 100,
            ulong.MaxValue - 80, ulong.MaxValue - 40, ulong.MaxValue - 60), 80,
            "Large absolute counters must calculate their small deltas without overflowing.");
        Expect(SensorMath.CpuPercent(ulong.MaxValue - 1, ulong.MaxValue - 1, ulong.MaxValue - 1, 0, 0, 0) is null,
            "Counter wrap/reset must begin a new baseline rather than become a normal zero reading.");
    }

    private static void CheckGpuCounters()
    {
        const string engine = "luid_0x00000000_0x00001234_phys_0_eng_0_engtype_3D";
        const string otherEngine = "luid_0x00000000_0x00001234_phys_0_eng_1_engtype_Copy";
        const string otherPhysical = "luid_0x00000000_0x00001234_phys_1_eng_0_engtype_3D";
        const string otherAdapter = "luid_0x00000000_0x00005678_phys_0_eng_0_engtype_3D";
        Near(SensorMath.GpuPercent(new[] { ($"pid_10_{engine}", 12d), ($"pid_37_{engine}", 37d) }), 49,
            "Per-process contributions to the same physical GPU engine must be summed.");
        Near(SensorMath.GpuPercent(new[] { ($"pid_10_{engine}", 12d), ($"pid_37_{engine}", 37d), ($"pid_2_{otherEngine}", 40d) }), 49,
            "Distinct GPU engines must use the busiest engine, not a sum of engine percentages.");
        Near(SensorMath.GpuPercent(new[] { ($"pid_10_{engine}", 60d), ($"pid_3_{otherPhysical}", 55d) }), 60,
            "Physical engine identifiers must separate distinct physical GPUs.");
        Near(SensorMath.GpuPercent(new[] { ($"pid_10_{engine}", 60d), ($"pid_3_{otherAdapter}", 75d) }), 75,
            "Adapter identifiers must remain separate when selecting the busiest engine.");
        Near(SensorMath.GpuPercent(new[] { ($"pid_10_{engine}", 20d), ($"PID_3_{engine.ToUpperInvariant()}", 25d) }), 45,
            "Case differences in PDH instance metadata must not split the same engine.");
        Near(SensorMath.GpuPercent(new[] { ($"pid_10_{engine}", 80d), ($"pid_37_{engine}", 40d) }), 100,
            "A sum beyond the utilization range must clamp to 100%.");
        Near(SensorMath.GpuPercent(new[] { ($"pid_10_{engine}", 0d) }), 0, "A real zero GPU counter must remain available.");
        Expect(SensorMath.GpuPercent(Array.Empty<(string, double)>()) is null, "An absent GPU counter set must not become zero.");
        Expect(SensorMath.GpuPercent(new[] { ("unrecognized instance", 50d) }) is null,
            "A counter without an adapter/engine identity must not be presented as device utilization.");
        var invalid = new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d, 101d };
        Expect(SensorMath.GpuPercent(invalid.Select(value => ($"pid_3_{engine}", value))) is null,
            "A GPU counter set containing only invalid values must remain unavailable.");
        Near(SensorMath.GpuPercent(invalid.Select(value => ($"pid_3_{engine}", value)).Append(($"pid_10_{engine}", 35d))), 35,
            "Invalid GPU contributions must not poison or inflate a valid engine reading.");
    }

    private static void CheckHistory()
    {
        var history = new TemperatureHistory();
        history.Add(Sample(0, Reading.Unavailable("test", "Missing first sample.")));
        Expect(history.Points.Count == 1 && history.Points[0].Temperature is null && history.Peak is null,
            "A missing first sample must leave a recorded gap and no zero peak.");
        history.Add(Sample(1, Reading.Available(62, "test")));
        history.Add(Sample(2, Reading.Available(48, "test")));
        history.MarkGap(Epoch.AddSeconds(3));
        history.Add(Sample(4, new Reading(90, MeasurementState.Unsupported, "test", "Unsupported payload.")));
        history.Add(Sample(5, new Reading(double.NaN, MeasurementState.Available, "test", "Invalid payload.")));
        Near(history.Peak, 62, "Only finite available temperature measurements may contribute to the peak.");
        Expect(history.Points[3].Temperature is null && history.Points[4].Temperature is null && history.Points[5].Temperature is null,
            "Explicit gaps, unsupported payloads, and invalid readings must remain gaps in history.");
        Expect(history.Points[3].Timestamp == Epoch.AddSeconds(3), "A gap must retain its actual sample timestamp.");
        var retained = history.Points.ToArray();
        history.ResetPeak();
        Expect(history.Peak is null && history.Points.SequenceEqual(retained), "Resetting the peak must preserve the complete temperature history.");
        history.MarkGap(Epoch.AddSeconds(6));
        Expect(history.Peak is null, "Missing readings after reset must not initialize a new zero peak.");
        history.Add(Sample(7, Reading.Available(35, "test")));
        Near(history.Peak, 35, "The first valid sample after reset must initialize the new peak.");

        var bounded = new TemperatureHistory();
        for (var index = 0; index < 207; index++)
            bounded.Add(Sample(index, index % 7 == 0 ? Reading.Unavailable("test", "Gap.") : Reading.Available(40 + index % 10, "test")));
        Expect(bounded.Points.Count == 180 && TemperatureHistory.Capacity == 180, "Long monitoring sessions must retain exactly the most recent 180 samples.");
        Expect(bounded.Points[0].Timestamp == Epoch.AddSeconds(27) && bounded.Points[^1].Timestamp == Epoch.AddSeconds(206),
            "History eviction must preserve chronological order and discard only the oldest samples.");
        Expect(bounded.Points.Single(point => point.Timestamp == Epoch.AddSeconds(28)).Temperature is null,
            "Capacity eviction must not fill missing points or change their timestamps.");
        Near(bounded.Peak, 49, "The session peak must survive history eviction.");
        bounded.Add(Sample(207, Reading.Available(80, "test")));
        for (var index = 208; index < 400; index++) bounded.Add(Sample(index, Reading.Available(40, "test")));
        Near(bounded.Peak, 80, "The session peak must remain valid after the peak sample itself leaves the retained graph.");
        Expect(!bounded.Points.Any(point => point.Timestamp == Epoch.AddSeconds(207)), "Peak retention must not extend the history capacity.");
    }

    private static void CheckStaleness()
    {
        var sample = Snapshot.Empty(Epoch);
        Expect(!sample.IsStale(Epoch.AddSeconds(6), 1), "The minimum stale threshold must allow exactly six seconds.");
        Expect(sample.IsStale(Epoch.AddSeconds(6).AddTicks(1), 1), "A sample beyond six seconds must be marked stale at the one-second interval.");
        Expect(!sample.IsStale(Epoch.AddSeconds(6), 2) && sample.IsStale(Epoch.AddSeconds(7), 2),
            "The default two-second interval must use a six-second stale threshold.");
        Expect(!sample.IsStale(Epoch.AddSeconds(15), 5) && sample.IsStale(Epoch.AddSeconds(16), 5),
            "Slower polling must use three update intervals as the stale threshold.");
        Expect(!sample.IsStale(Epoch.AddSeconds(30), 10) && sample.IsStale(Epoch.AddSeconds(31), 10),
            "A ten-second interval must not falsely mark a sample stale before thirty seconds.");
        Expect(!sample.IsStale(Epoch.AddSeconds(-1), 2), "A backwards wall-clock adjustment must not create a stale sample immediately.");
    }

    private static void CheckPreferences()
    {
        var defaults = new Preferences();
        Expect(defaults.UpdateInterval == 2 && defaults.Appearance == Appearance.Dark && !defaults.Compact && !defaults.AlwaysOnTop && defaults.Animations,
            "Default preferences must match the macOS panel's display, polling, and motion defaults.");
        Expect(!defaults.LibreHardwareMonitorEnabled, "Optional sensor access must remain disabled until explicitly selected.");
        foreach (var interval in new[] { 1, 2, 5, 10 })
            Expect((defaults with { UpdateInterval = interval }).Normalize().UpdateInterval == interval, $"Supported interval {interval} must survive normalization.");
        foreach (var interval in new[] { int.MinValue, -1, 0, 3, int.MaxValue })
            Expect((defaults with { UpdateInterval = interval }).Normalize().UpdateInterval == 2, $"Invalid interval {interval} must recover to two seconds.");
        Expect((defaults with { Appearance = (Appearance)999 }).Normalize().Appearance == Appearance.Dark,
            "Unknown appearance values must recover to Dark.");
        var negativeOrigin = new PanelFrame(-1400, -80, 480);
        Expect((defaults with { DetailFrame = negativeOrigin }).Normalize().DetailFrame == negativeOrigin,
            "Finite negative display coordinates must be preserved for secondary displays.");
        Expect((defaults with { DetailFrame = new PanelFrame(0, 0, 1), CompactFrame = new PanelFrame(10, 20, 9000) }).Normalize() is
            { DetailFrame.Size: 350, CompactFrame.Size: 1600 }, "Finite frame sizes must clamp to the usable bounds.");
        Expect((defaults with { DetailFrame = new PanelFrame(0, 0, -1) }).Normalize().DetailFrame?.Size == 350,
            "A negative stored size must recover to a usable minimum.");
        foreach (var nonfinite in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            foreach (var frame in new[] { new PanelFrame(nonfinite, 0, 480), new PanelFrame(0, nonfinite, 480), new PanelFrame(0, 0, nonfinite) })
                Expect((defaults with { DetailFrame = frame, CompactFrame = frame }).Normalize() is { DetailFrame: null, CompactFrame: null },
                    "A non-finite saved coordinate or size must invalidate both persisted frame variants.");
        }

        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"thermal kun checks-{Guid.NewGuid():N}");
        var path = System.IO.Path.Combine(directory, "nested", "preferences.json");
        var store = new PreferencesStore(path);
        try
        {
            Expect(store.Load() == defaults, "A missing preferences file must recover to safe defaults.");
            var saved = defaults with { Appearance = Appearance.Light, Compact = true, AlwaysOnTop = true, Animations = false,
                UpdateInterval = 5, LibreHardwareMonitorEnabled = true, DetailFrame = negativeOrigin, CompactFrame = new PanelFrame(30, 50, 360) };
            store.Save(saved);
            Expect(new PreferencesStore(path).Load() == saved, "A fresh store must restore all saved choices and both frame variants.");
            Expect(!File.Exists(path + ".tmp"), "A successful atomic save must not leave a temporary preferences file behind.");
            var replacement = saved with { Compact = false, UpdateInterval = 10, LibreHardwareMonitorEnabled = false };
            store.Save(replacement);
            Expect(new PreferencesStore(path).Load() == replacement, "Replacing a preferences file must preserve the latest choices across store reconstruction.");

            File.WriteAllText(path, "{\"Compact\":true,\"UpdateInterval\":5,\"futureOption\":\"retained-by-future-version\"}");
            var partial = store.Load();
            Expect(partial.Compact && partial.UpdateInterval == 5 && partial.Appearance == Appearance.Dark && partial.Animations,
                "Partial legacy preferences must preserve supplied choices and use defaults for absent fields.");
            File.WriteAllText(path, "{\"UpdateInterval\":3,\"Appearance\":999,\"DetailFrame\":{\"X\":-500,\"Y\":70,\"Size\":2}}");
            Expect(store.Load() is { UpdateInterval: 2, Appearance: Appearance.Dark, DetailFrame.X: -500, DetailFrame.Size: 350 },
                "Load must normalize invalid intervals, unknown appearance, and undersized frames.");
            foreach (var malformed in new[] { "", "{broken", "null", "[]", "{\"UpdateInterval\":\"fast\"}", "{\"DetailFrame\":{\"X\":\"NaN\"}}" })
            {
                File.WriteAllText(path, malformed);
                Expect(store.Load() == defaults, "Malformed, null, or incompatible preferences must recover without an exception.");
            }
            var invalid = saved with { UpdateInterval = 0, DetailFrame = new PanelFrame(double.NaN, 0, 480) };
            store.Save(invalid);
            Expect(new PreferencesStore(path).Load() == invalid.Normalize(), "Save must normalize invalid in-memory data before serialization and reload.");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        Expect(!Directory.Exists(directory), "Preferences checks must remove only their isolated temporary directory.");
    }

    private static void CheckTemperatureDiscovery()
    {
        // Discovery fixtures resemble LHM data.json. The display Value may be Fahrenheit; only explicitly Celsius RawValue is eligible.
        object Sensor(string id, string name, string? raw, string type = "Temperature", string value = "999 °F") =>
            new { SensorId = id, Text = name, RawValue = raw, Value = value, Type = type, Children = Array.Empty<object>() };
        string Tree(string hardwareId, params object[] children) => JsonSerializer.Serialize(
            new { Text = "Sensor", Children = new[] { new { HardwareId = hardwareId, Children = children } } });
        Reading Read(string hardwareId, params object[] children) => NativeSensors.ParseLibreHardwareMonitorJson(Tree(hardwareId, children));

        Near(Read("/intelcpu/0", Sensor("/intelcpu/0/temperature/0", "CPU Package", "52.5 °C")).Value, 52.5,
            "Discovery must use native Celsius RawValue even when display Value is Fahrenheit.");
        Near(Read("/intelcpu/0", Sensor("/intelcpu/0/temperature/0", "CPU Package", "52,5 °C")).Value, 52.5,
            "A single decimal comma in provider-formatted Celsius must be accepted.");
        Near(Read("/intelcpu/0", Sensor("/intelcpu/0/temperature/0", "CPU Package", "  +52.5 °C  ")).Value, 52.5,
            "Celsius RawValue may contain outer whitespace and a leading sign.");
        foreach (var invalidRaw in new string?[] { null, "-", "52.5 °F", "52.5 K", "52.5", "NaN °C", "Infinity °C", "-Infinity °C",
                     "151.0 °C", "-20.1 °C", " °C", "1,234.5 °C", "1.234,5 °C", "5e1 °C" })
        {
            var reading = Read("/intelcpu/0", Sensor("/intelcpu/0/temperature/0", "CPU Package", invalidRaw, value: "52.5 °C"));
            Expect(!reading.IsAvailable && reading.Value is null && reading.State == MeasurementState.Unavailable,
                $"Invalid or non-Celsius raw value must never fall back to display Value: {invalidRaw ?? "null"}");
        }
        Near(Read("/intelcpu/0", Sensor("/intelcpu/0/temperature/0", "CPU Package", "-20.0 °C")).Value, -20,
            "The supported Celsius range must include its lower boundary.");
        Near(Read("/intelcpu/0", Sensor("/intelcpu/0/temperature/0", "CPU Package", "150.0 °C")).Value, 150,
            "The supported Celsius range must include its upper boundary.");

        Expect(!Read("/gpu-nvidia/0", Sensor("/gpu-nvidia/0/temperature/0", "CPU Package", "52.5 °C")).IsAvailable,
            "A GPU cannot become a CPU source merely through its label.");
        Expect(!Read("/intelcpu/0", Sensor("/gpu-nvidia/0/temperature/0", "CPU Package", "52.5 °C")).IsAvailable,
            "A CPU parent must not authorize a mismatched GPU sensor identity.");
        Expect(!Read("/intelcpu/0", Sensor("/intelcpu/1/temperature/0", "CPU Package", "52.5 °C")).IsAvailable,
            "The sensor identifier must match the exact CPU parent's hardware identity.");
        foreach (var parent in new[] { "/lpc/it87/0", "/acpi/thermalzone/0", "/intelcpu/renamed", "/amdcpu/0/child", "/cpu/0" })
            Expect(!Read(parent, Sensor(parent + "/temperature/0", "CPU Package", "52.5 °C")).IsAvailable,
                $"Ambient, ACPI or unrecognized hardware identity must be excluded: {parent}");
        foreach (var id in new[] { "/intelcpu/0/load/0", "/intelcpu/0/temperature/", "/intelcpu/0/temperature/x", "/intelcpu/0/temperature/0/child" })
            Expect(!Read("/intelcpu/0", Sensor(id, "CPU Package", "52.5 °C")).IsAvailable,
                $"Unknown/non-temperature sensor identifier must be excluded: {id}");
        Expect(!Read("/intelcpu/0", Sensor("/intelcpu/0/temperature/0", "CPU Package", "52.5 °C", type: "Load")).IsAvailable,
            "The declared sensor type must be Temperature even with a matching ID and CPU label.");
        foreach (var label in new[] { "CPU Core #1 Distance to TjMax", "Distance to TjMax", "Core (Tctl)", "CPU (Tctl)", "Ambient", "CPU Package renamed" })
            Expect(!Read("/amdcpu/0", Sensor("/amdcpu/0/temperature/0", label, "52.5 °C")).IsAvailable,
                $"A delta, offset-only Tctl or unverified label must be excluded: {label}");
        var nestedOtherHardware = new
        {
            HardwareId = "/gpu-nvidia/0",
            Children = new[] { Sensor("/intelcpu/0/temperature/0", "CPU Package", "52.5 °C") }
        };
        Expect(!Read("/intelcpu/0", nestedOtherHardware).IsAvailable,
            "A nested non-CPU hardware node must clear inherited CPU identity.");

        Near(Read("/amdcpu/0", Sensor("/amdcpu/0/temperature/0", "Core (Tdie)", "45.5 °C"),
            Sensor("/amdcpu/0/temperature/1", "Core (Tctl/Tdie)", "62.0 °C")).Value, 45.5,
            "Native die temperature must be preferred to the combined control/die label.");
        Near(Read("/intelcpu/0", Sensor("/intelcpu/0/temperature/0", "CPU Package", "70.0 °C"),
            Sensor("/intelcpu/0/temperature/1", "CPU Core #1", "95.0 °C")).Value, 70,
            "A valid package reading must take precedence over a hotter core label.");
        var coreMean = Read("/intelcpu/0", Sensor("/intelcpu/0/temperature/0", "CPU Package", "-"),
            Sensor("/intelcpu/0/temperature/1", "P-Core #1", "40.0 °C"),
            Sensor("/intelcpu/0/temperature/2", "E-Core #2", "60.0 °C"));
        Near(coreMean.Value, 50, "A missing package reading must fall back to the arithmetic mean of recognized mapped cores.");
        Expect(coreMean.Detail.Contains("Mean of mapped CPU cores", StringComparison.Ordinal) &&
               coreMean.Detail.Contains("/intelcpu/0/temperature/1", StringComparison.Ordinal) &&
               coreMean.Detail.Contains("/intelcpu/0/temperature/2", StringComparison.Ordinal),
            "A core mean must disclose its aggregation and contributing sensor identities.");
        var hottestPackage = Read("/intelcpu/0", Sensor("/intelcpu/0/temperature/0", "CPU Package", "60.0 °C"),
            Sensor("/intelcpu/0/temperature/1", "CPU Package", "72.0 °C"));
        Near(hottestPackage.Value, 72, "Multiple preferred package sources must select the hottest measured package.");
        Expect(hottestPackage.Detail.Contains("preferred label priority", StringComparison.Ordinal),
            "Package aggregation must explain that it selects a preferred measured source.");
        Near(Read("/intelcpu/0", Sensor("/intelcpu/0/temperature/0", "CPU Core #1", "30.0 °C"),
            Sensor("/INTELCPU/0/TEMPERATURE/0", "CPU Core #1", "30.0 °C"),
            Sensor("/intelcpu/0/temperature/1", "CPU Core #2", "90.0 °C")).Value, 60,
            "Duplicate sensor identities, including case variants, must contribute only once to a core mean.");

        foreach (var malformed in new[] { "[]", "null", "42", "{", "{\"Children\":true}", "{\"HardwareId\":\"/intelcpu/0\",\"Children\":{}}" })
            Expect(!NativeSensors.ParseLibreHardwareMonitorJson(malformed).IsAvailable,
                "Malformed or non-tree discovery JSON must remain unavailable.");
        var overBudget = Tree("/intelcpu/0", Sensor("/intelcpu/0/temperature/0", "CPU Package", "52.5 °C")) + new string(' ', 1024 * 1024);
        var bounded = NativeSensors.ParseLibreHardwareMonitorJson(overBudget);
        Expect(!bounded.IsAvailable && bounded.Detail.Contains("too large", StringComparison.OrdinalIgnoreCase),
            "Discovery bodies beyond the one-MiB budget must be rejected before selecting a valid sensor.");
        var manySensors = Enumerable.Range(0, 257).Select(index =>
            Sensor($"/intelcpu/0/temperature/{index}", $"CPU Core #{index}", "52.5 °C")).ToArray();
        var overCount = Read("/intelcpu/0", manySensors);
        Expect(!overCount.IsAvailable && overCount.Detail.Contains("Too many", StringComparison.Ordinal),
            "A discovery response with more than 256 CPU sensor identities must be rejected as a bounded-sample limit.");
        var tooDeep = string.Concat(Enumerable.Repeat("{\"Children\":[", 65)) + "{}" + string.Concat(Enumerable.Repeat("]}", 65));
        Expect(!NativeSensors.ParseLibreHardwareMonitorJson(tooDeep).IsAvailable,
            "Excessively nested discovery JSON must be rejected without recursive traversal failure.");
    }

    private static void CheckTemperatureSensorGet()
    {
        // Actual LHM GET success has no result member. A POST-style explicit result is optional and must be successful when present.
        Near(NativeSensors.ParseLibreHardwareMonitorSensorJson("{\"value\":42.125,\"min\":40,\"max\":50,\"format\":\"{0:F1} °C\"}"), 42.125,
            "The actual numeric Sensor GET schema without result must be accepted.");
        Near(NativeSensors.ParseLibreHardwareMonitorSensorJson("{\"result\":\"ok\",\"value\":42.125,\"format\":\"{0:F1} °C\"}"), 42.125,
            "An optional explicit success result must be accepted.");
        Near(NativeSensors.ParseLibreHardwareMonitorSensorJson("{\"value\":-20,\"format\":\"{0:F1} °C\"}"), -20,
            "Numeric Sensor GET must accept the lower supported Celsius boundary.");
        Near(NativeSensors.ParseLibreHardwareMonitorSensorJson("{\"value\":150,\"format\":\"{0:F1} °C\"}"), 150,
            "Numeric Sensor GET must accept the upper supported Celsius boundary.");
        foreach (var response in new[]
                 {
                     "{\"result\":\"fail\",\"value\":42,\"format\":\"{0:F1} °C\"}",
                     "{\"result\":null,\"value\":42,\"format\":\"{0:F1} °C\"}",
                     "{\"result\":true,\"value\":42,\"format\":\"{0:F1} °C\"}",
                     "{\"value\":null,\"format\":\"{0:F1} °C\"}",
                     "{\"value\":\"42\",\"format\":\"{0:F1} °C\"}",
                     "{\"value\":\"NaN\",\"format\":\"{0:F1} °C\"}",
                     "{\"value\":1e400,\"format\":\"{0:F1} °C\"}",
                     "{\"value\":-1e400,\"format\":\"{0:F1} °C\"}",
                     "{\"value\":151,\"format\":\"{0:F1} °C\"}",
                     "{\"value\":-20.1,\"format\":\"{0:F1} °C\"}",
                     "{\"value\":42,\"format\":\"{0:F1} °F\"}",
                     "{\"value\":42,\"format\":\"{0:F1} K\"}",
                     "{\"value\":42,\"format\":\"{0:F1}\"}",
                     "{\"value\":42}",
                     "{\"value\":42,\"format\":null}",
                     "{}", "[]", "null", "{", "{\"value\":NaN,\"format\":\"{0:F1} °C\"}"
                 })
            Expect(NativeSensors.ParseLibreHardwareMonitorSensorJson(response) is null,
                $"Failed, missing, nonnumeric, non-finite, out-of-range or non-Celsius Sensor GET must be rejected: {response}");
        var overBudget = "{\"value\":42,\"format\":\"{0:F1} °C\"}" + new string(' ', 1024 * 1024);
        Expect(NativeSensors.ParseLibreHardwareMonitorSensorJson(overBudget) is null,
            "Numeric Sensor GET bodies exceeding the shared byte budget must remain unavailable.");
    }

    private static void CheckTemperatureTransport()
    {
        // Invoke only the HTTP adapter, with an in-memory handler. Sample is deliberately not used: it would call native Windows APIs on Windows.
        const string cpuTree = "{\"Children\":[{\"HardwareId\":\"/intelcpu/0\",\"Children\":[{\"SensorId\":\"/intelcpu/0/temperature/0\",\"Text\":\"CPU Package\",\"RawValue\":\"999 °F\",\"Value\":\"999 °F\",\"Type\":\"Temperature\",\"Children\":[]}]}]}";
        const string gpuTree = "{\"Children\":[{\"HardwareId\":\"/gpu-nvidia/0\",\"Children\":[{\"SensorId\":\"/gpu-nvidia/0/temperature/0\",\"Text\":\"CPU Package\",\"RawValue\":\"50 °C\",\"Type\":\"Temperature\",\"Children\":[]}]}]}";
        using (var native = new NativeSensors())
        {
            var handler = new FakeTemperatureHandler(cpuTree);
            InjectHttp(native, handler);
            var reading = ReadTemperatureOnly(native);
            Near(reading.Value, 48.125, "The temperature adapter must use numeric Sensor GET rather than discovery display/raw text.");
            Expect(handler.Requests.Count == 2 && handler.Requests.All(item => item.Method == HttpMethod.Get &&
                item.Uri.Scheme == "http" && item.Uri.Host == "127.0.0.1" && item.Uri.Port == 8085),
                "Temperature discovery and sensor reads must emit only fixed loopback GETs.");
            Expect(handler.Requests[0].Uri.AbsolutePath == "/data.json" && handler.Requests[1].Uri.AbsolutePath == "/Sensor" &&
                handler.Requests[1].Uri.Query.Contains("action=Get", StringComparison.Ordinal) &&
                Uri.UnescapeDataString(handler.Requests[1].Uri.Query).Contains("id=/intelcpu/0/temperature/0", StringComparison.Ordinal),
                "The adapter must request the discovered CPU sensor identity through the read-only Get action.");
            Expect(!handler.Requests.Any(item => item.Uri.Query.Contains("Set", StringComparison.OrdinalIgnoreCase) ||
                item.Uri.Query.Contains("Reset", StringComparison.OrdinalIgnoreCase)),
                "The temperature adapter must never issue sensor mutation actions.");
            Expect(reading.Detail.Contains("/intelcpu/0/temperature/0", StringComparison.Ordinal) &&
                reading.Detail.Contains("48.125", StringComparison.Ordinal) && reading.Detail.Contains("timestamp", StringComparison.Ordinal),
                "Temperature diagnostics must retain sensor identity, measured value, and the acquisition timestamp limitation.");
            native.Dispose();
            Expect(handler.Disposed, "Releasing the backend must dispose its optional HTTP client without installing or starting a service.");
        }
        using (var native = new NativeSensors())
        {
            var handler = new FakeTemperatureHandler(gpuTree);
            InjectHttp(native, handler);
            Expect(!ReadTemperatureOnly(native).IsAvailable && handler.Requests.Count == 1,
                "Unrecognized CPU hardware must not trigger subsequent sensor GETs.");
        }
        using (var native = new NativeSensors())
        {
            var handler = new FakeTemperatureHandler("{}") { Oversize = true };
            InjectHttp(native, handler);
            Expect(!ReadTemperatureOnly(native).IsAvailable,
                "An oversized HTTP response must become unavailable rather than escape as an exception.");
        }
        using (var native = new NativeSensors())
        {
            var handler = new FakeTemperatureHandler(cpuTree)
            {
                SensorBody = FakeTemperatureHandler.NumericSensor + new string(' ', 1024 * 1024 - 100 - FakeTemperatureHandler.NumericSensor.Length)
            };
            InjectHttp(native, handler);
            Expect(!ReadTemperatureOnly(native).IsAvailable && handler.Requests.Count == 2,
                "Discovery and Sensor GET must share one byte budget, even when each response individually fits below one MiB.");
        }
        using (var native = new NativeSensors())
        {
            var handler = new FakeTemperatureHandler(cpuTree) { Status = HttpStatusCode.Found };
            InjectHttp(native, handler);
            Expect(!ReadTemperatureOnly(native).IsAvailable && handler.Requests.Count == 1,
                "An HTTP redirect must be rejected before any redirected or sensor request is issued.");
        }
        using (var native = new NativeSensors())
        {
            var handler = new FakeTemperatureHandler("{}") { Delay = TimeSpan.FromSeconds(5) };
            InjectHttp(native, handler);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Expect(!ReadTemperatureOnly(native).IsAvailable && handler.CancellationObserved && timer.Elapsed < TimeSpan.FromSeconds(3),
                "An unresponsive local source must observe cancellation within the shared two-second deadline.");
        }
        using (var native = new NativeSensors())
        {
            var handler = new FakeTemperatureHandler(cpuTree) { Delay = TimeSpan.FromMilliseconds(1100) };
            InjectHttp(native, handler);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Expect(!ReadTemperatureOnly(native).IsAvailable && handler.Requests.Count == 2 && handler.CancellationObserved &&
                timer.Elapsed < TimeSpan.FromSeconds(3),
                "The sensor read must reuse the discovery deadline rather than receive a fresh two-second timeout.");
        }
    }

    private static void InjectHttp(NativeSensors native, HttpMessageHandler handler)
    {
        var field = typeof(NativeSensors).GetField("localHttp", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Temperature HTTP test injection point was not found.");
        field.SetValue(native, new HttpClient(handler));
    }

    private static Reading ReadTemperatureOnly(NativeSensors native)
    {
        var method = typeof(NativeSensors).GetMethod("ReadTemperature", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Temperature-only test method was not found.");
        return method.Invoke(native, null) as Reading
            ?? throw new InvalidOperationException("Temperature-only test did not return a Reading.");
    }

    private sealed class FakeTemperatureHandler(string tree) : HttpMessageHandler
    {
        public const string NumericSensor = "{\"value\":48.125,\"format\":\"{0:F1} °C\"}";
        public List<(HttpMethod Method, Uri Uri)> Requests { get; } = new();
        public bool Oversize { get; init; }
        public TimeSpan Delay { get; init; }
        public string SensorBody { get; init; } = NumericSensor;
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public bool Disposed { get; private set; }
        public bool CancellationObserved { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("The adapter supplied no request URI.");
            Requests.Add((request.Method, uri));
            try
            {
                if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
            var content = new StringContent(uri.AbsolutePath == "/data.json" ? tree : SensorBody, Encoding.UTF8, "application/json");
            if (Oversize) content.Headers.ContentLength = 1024 * 1024 + 1;
            var response = new HttpResponseMessage(Status) { Content = content };
            if (Status == HttpStatusCode.Found) response.Headers.Location = new Uri("https://example.invalid/sensor");
            return response;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}

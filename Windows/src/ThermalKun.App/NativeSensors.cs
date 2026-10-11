using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ThermalKun.Core;

namespace ThermalKun.App;

/// <summary>
/// Read-only measurement sources. Call Sample, Reset and Dispose serially on the sampling worker.
/// No native sensor driver, process, elevated operation, service or firewall rule is installed here.
/// </summary>
public sealed class NativeSensors : IDisposable
{
    private readonly Func<Reading>? bundledTemperature;
    public NativeSensors(Func<Reading>? bundledTemperature = null) => this.bundledTemperature = bundledTemperature;
    private const string CpuSource = "GetSystemTimes";
    private const string MemorySource = "GlobalMemoryStatusEx";
    private const string GpuSource = "PDH GPU Engine";
    private const string TemperatureSource = "LibreHardwareMonitor local HTTP";
    private const string GpuCounterPath = @"\GPU Engine(*)\Utilization Percentage";
    private const uint PdhMoreData = 0x800007d2;
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhFmtNoCap100 = 0x00008000;
    private const int MaxHttpBytes = 1024 * 1024;
    private const int MaxPdhBytes = 16 * 1024 * 1024;
    private const int MaxTemperatureSensors = 256;
    private static readonly Uri LocalServer = new("http://127.0.0.1:8085/");
    private static readonly TimeSpan BaselineMaxAge = TimeSpan.FromSeconds(30);
    private static readonly JsonDocumentOptions JsonOptions = new() { MaxDepth = 64 };

    private (ulong Idle, ulong Kernel, ulong User)? cpuPrevious;
    private long lastSampleTicks;
    private long nextGpuAttempt;
    private IntPtr gpuQuery;
    private IntPtr gpuCounter;
    private bool gpuPrimed;
    private string gpuFailure = "Waiting for two counter samples.";
    private HttpClient? localHttp;
    private bool disposed;

    public Snapshot Sample(bool enableLibreHardwareMonitor)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var ticks = Stopwatch.GetTimestamp();
        if (lastSampleTicks != 0 && Stopwatch.GetElapsedTime(lastSampleTicks, ticks) > BaselineMaxAge)
            Reset();
        lastSampleTicks = ticks;

        var empty = Snapshot.Empty(DateTimeOffset.UtcNow);
        var cpu = ReadCpu();
        var gpu = ReadGpu(ticks);
        var (used, total) = ReadMemory();
        if (!enableLibreHardwareMonitor && localHttp is not null)
        {
            localHttp.Dispose();
            localHttp = null;
        }
        var temperature = enableLibreHardwareMonitor
            ? ReadTemperature()
            : bundledTemperature?.Invoke() ?? Reading.Unsupported(TemperatureSource,
                "Optional source is off. This app does not start LibreHardwareMonitor or install its driver. Enable only after configuring its local server yourself.");
        return new Snapshot(DateTimeOffset.UtcNow, temperature, cpu, gpu, used, total,
            empty.ThermalStatus, empty.MemoryPressure);
    }

    /// <summary>Discard rate-counter baselines after resume or a long sampling pause.</summary>
    public void Reset()
    {
        cpuPrevious = null;
        lastSampleTicks = 0;
        nextGpuAttempt = 0;
        CloseGpuQuery();
        gpuFailure = "Waiting for two counter samples after reset.";
    }

    private Reading ReadCpu()
    {
        if (!OperatingSystem.IsWindows()) return Reading.Unsupported(CpuSource, "Windows API is unavailable on this platform.");
        try
        {
            // GetSystemTimes otherwise silently measures just the caller's group on machines with >64 CPUs.
            var processorCount = GetActiveProcessorCount(0xffff);
            if (processorCount == 0)
            {
                cpuPrevious = null;
                return Reading.Unavailable(CpuSource, "Unable to identify active processor groups; CPU scope cannot be verified.");
            }
            if (processorCount > 64)
            {
                cpuPrevious = null;
                return Reading.Unsupported(CpuSource,
                    "More than 64 logical processors: GetSystemTimes only covers the caller's processor group. Whole-machine group aggregation is not implemented.");
            }
            if (!GetSystemTimes(out var idle, out var kernel, out var user))
            {
                cpuPrevious = null;
                return Reading.Unavailable(CpuSource, NativeError("GetSystemTimes"));
            }
            var current = (Idle: idle.Value, Kernel: kernel.Value, User: user.Value);
            var previous = cpuPrevious;
            cpuPrevious = current;
            if (previous is null) return Reading.Unavailable(CpuSource, "Waiting for a second counter sample.");
            var value = SensorMath.CpuPercent(previous.Value.Idle, previous.Value.Kernel, previous.Value.User,
                current.Idle, current.Kernel, current.User);
            return value is double percent
                ? Reading.Available(percent, CpuSource, "Busy share of logical processor time. Kernel time includes idle; idle time is subtracted.")
                : Reading.Unavailable(CpuSource, "Counter reset, invalid delta or zero elapsed processor time; baseline restarted.");
        }
        catch (Exception ex) when (IsMissingNativeApi(ex))
        {
            cpuPrevious = null;
            return Reading.Unavailable(CpuSource, ApiFailure(ex));
        }
    }

    private static (Reading Used, Reading Total) ReadMemory()
    {
        if (!OperatingSystem.IsWindows())
        {
            var missing = Reading.Unsupported(MemorySource, "Windows API is unavailable on this platform.");
            return (missing, missing);
        }
        try
        {
            var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            if (!GlobalMemoryStatusEx(ref status))
            {
                var missing = Reading.Unavailable(MemorySource, NativeError("GlobalMemoryStatusEx"));
                return (missing, missing);
            }
            if (status.TotalPhysical == 0 || status.AvailablePhysical > status.TotalPhysical)
            {
                var missing = Reading.Unavailable(MemorySource, "Invalid physical memory totals rejected.");
                return (missing, missing);
            }
            const double gibibyte = 1024d * 1024 * 1024;
            return (Reading.Available((status.TotalPhysical - status.AvailablePhysical) / gibibyte, MemorySource,
                        "Total physical memory minus available physical memory; GiB. This is not a memory-pressure state."),
                    Reading.Available(status.TotalPhysical / gibibyte, MemorySource, "Total physical memory; GiB."));
        }
        catch (Exception ex) when (IsMissingNativeApi(ex))
        {
            var missing = Reading.Unavailable(MemorySource, ApiFailure(ex));
            return (missing, missing);
        }
    }

    private Reading ReadGpu(long ticks)
    {
        if (!OperatingSystem.IsWindows()) return Reading.Unsupported(GpuSource, "Windows API is unavailable on this platform.");
        try
        {
            if (gpuQuery == IntPtr.Zero)
            {
                if (ticks < nextGpuAttempt) return Reading.Unavailable(GpuSource, gpuFailure);
                nextGpuAttempt = ticks + 30 * Stopwatch.Frequency;
                var status = PdhOpenQueryW(null, UIntPtr.Zero, out gpuQuery);
                if (status != 0)
                {
                    CloseGpuQuery();
                    return GpuError("PdhOpenQuery", status);
                }
                status = PdhAddEnglishCounterW(gpuQuery, GpuCounterPath, UIntPtr.Zero, out gpuCounter);
                if (status != 0)
                {
                    CloseGpuQuery();
                    return GpuError("PdhAddEnglishCounter", status);
                }
            }

            var collectStatus = PdhCollectQueryData(gpuQuery);
            if (collectStatus != 0)
            {
                gpuPrimed = false;
                return GpuError("PdhCollectQueryData", collectStatus);
            }
            if (!gpuPrimed)
            {
                gpuPrimed = true;
                return Reading.Unavailable(GpuSource, "Waiting for a second GPU counter sample.");
            }
            // Probe afresh on retries: instance creation/removal can change the required buffer size.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                uint bytes = 0;
                var status = PdhGetFormattedCounterArrayW(gpuCounter, PdhFmtDouble | PdhFmtNoCap100,
                    ref bytes, out _, IntPtr.Zero);
                if (status == 0 && bytes == 0) return Reading.Unavailable(GpuSource, "No GPU engine instances were returned.");
                if (status != PdhMoreData) return GpuError("PdhGetFormattedCounterArray size", status);
                if (bytes == 0 || bytes > MaxPdhBytes)
                    return Reading.Unavailable(GpuSource, "GPU counter array exceeds the supported size.");
                var buffer = Marshal.AllocHGlobal((int)bytes);
                try
                {
                    var allocatedBytes = bytes;
                    status = PdhGetFormattedCounterArrayW(gpuCounter, PdhFmtDouble | PdhFmtNoCap100,
                        ref bytes, out var count, buffer);
                    if (status == PdhMoreData) continue;
                    if (status != 0) return GpuError("PdhGetFormattedCounterArray", status);
                    var stride = Marshal.SizeOf<PdhCounterItem>();
                    if ((ulong)count * (ulong)stride > allocatedBytes)
                        return Reading.Unavailable(GpuSource, "Invalid GPU counter array length rejected.");
                    var values = new List<(string Instance, double Value)>((int)count);
                    for (var i = 0; i < count; i++)
                    {
                        var item = Marshal.PtrToStructure<PdhCounterItem>(IntPtr.Add(buffer, checked(i * stride)));
                        if (item.Value.Status is not (0 or 1) || item.Name == IntPtr.Zero) continue;
                        var name = Marshal.PtrToStringUni(item.Name);
                        if (name is not null) values.Add((name, item.Value.DoubleValue));
                    }
                    var value = SensorMath.GpuPercent(values);
                    return value is double percent
                        ? Reading.Available(percent, GpuSource,
                            $"Busiest GPU engine across adapters; same-engine per-process counters are summed. {values.Count} valid counter statuses. Not an average across engines.")
                        : Reading.Unavailable(GpuSource, "No valid GPU engine measurements. Driver support, active instances or performance-counter access may be absent.");
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            return Reading.Unavailable(GpuSource, "GPU instances changed repeatedly during collection; retrying on the next sample.");
        }
        catch (Exception ex) when (IsMissingNativeApi(ex))
        {
            gpuFailure = ApiFailure(ex);
            CloseGpuQuery();
            return Reading.Unavailable(GpuSource, gpuFailure);
        }
    }

    private Reading GpuError(string operation, uint status)
    {
        gpuFailure = $"{operation} failed (0x{status:X8}). GPU counters may be missing, unsupported by the driver, or inaccessible.";
        return Reading.Unavailable(GpuSource, gpuFailure);
    }

    private Reading ReadTemperature()
    {
        // Both discovery and all selected sensor GETs share the same deadline and total byte budget.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var remainingBytes = MaxHttpBytes;
        try
        {
            localHttp ??= CreateLocalHttpClient();
            using var tree = GetLocalJson("data.json", deadline.Token, ref remainingBytes);
            var candidates = FindTemperatureSensors(tree.RootElement);
            if (candidates.Count == 0)
                return Reading.Unavailable(TemperatureSource,
                    "No supported CPU temperature sensor: require matching Intel/AMD CPU hardware and sensor identifiers and a recognized package, Tdie or core label. Renamed/ambient/GPU sensors are not substituted.");
            if (candidates.Count > MaxTemperatureSensors)
                return Reading.Unavailable(TemperatureSource, "Too many CPU sensors for a bounded local sample.");

            foreach (var group in candidates.GroupBy(sensor => sensor.Rank))
            {
                var readings = new List<(TemperatureSensor Sensor, double Value)>();
                foreach (var sensor in group)
                {
                    using var response = GetLocalJson("Sensor?action=Get&id=" + Uri.EscapeDataString(sensor.Id),
                        deadline.Token, ref remainingBytes);
                    var value = ReadSensorResponse(response.RootElement);
                    if (value is double valid) readings.Add((sensor, valid));
                }
                if (readings.Count > 0)
                    return BuildTemperatureReading(group.Key == 4, readings, group.Count(),
                        "Read-only /Sensor?action=Get returns native Celsius. Server acquisition timestamp is unavailable; the app timestamp records receipt only.");
            }
            return Reading.Unavailable(TemperatureSource, "No valid CPU temperature values; null/non-finite/outside -20..150 °C or unsupported unit rejected.");
        }
        catch (OperationCanceledException)
        {
            return Reading.Unavailable(TemperatureSource, "The optional local source did not complete within the shared 2-second budget.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException or InvalidOperationException or NotSupportedException)
        {
            return Reading.Unavailable(TemperatureSource,
                $"Local source unavailable ({ex.GetType().Name}). Configure LibreHardwareMonitor yourself at 127.0.0.1:8085. No server, driver or elevated operation is started by this app.");
        }
    }

    private static HttpClient CreateLocalHttpClient() => new(new SocketsHttpHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        UseCookies = false,
        Credentials = null,
        ConnectTimeout = TimeSpan.FromSeconds(2),
        MaxResponseHeadersLength = 16,
        AutomaticDecompression = DecompressionMethods.None
    }) { Timeout = Timeout.InfiniteTimeSpan };

    private JsonDocument GetLocalJson(string relativePath, CancellationToken token, ref int remainingBytes)
    {
        // relativePath is generated internally; neither discovery URLs nor redirects are followed.
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(LocalServer, relativePath));
        request.Headers.Accept.ParseAdd("application/json");
        using var response = localHttp!.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).GetAwaiter().GetResult();
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException("The local source returned an unsuccessful status.", null, response.StatusCode);
        if (response.Content.Headers.ContentLength is long length && length > remainingBytes)
            throw new InvalidDataException("Local response exceeds the shared byte budget.");
        using var stream = response.Content.ReadAsStreamAsync(token).GetAwaiter().GetResult();
        using var bytes = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var count = stream.ReadAsync(chunk.AsMemory(), token).AsTask().GetAwaiter().GetResult();
            if (count == 0) break;
            if (count > remainingBytes) throw new InvalidDataException("Local response exceeds the shared byte budget.");
            remainingBytes -= count;
            bytes.Write(chunk, 0, count);
        }
        token.ThrowIfCancellationRequested();
        return JsonDocument.Parse(bytes.ToArray(), JsonOptions);
    }

    // Pure helpers let fixture tests verify identity/unit/null handling on the Mac build host.
    // Live sampling uses numeric Sensor GETs; this helper only accepts explicit Celsius RawValue strings.
    internal static Reading ParseLibreHardwareMonitorJson(string json)
    {
        try
        {
            if (Encoding.UTF8.GetByteCount(json) > MaxHttpBytes)
                return Reading.Unavailable(TemperatureSource, "Response too large.");
            using var document = JsonDocument.Parse(json, JsonOptions);
            var candidates = FindTemperatureSensors(document.RootElement);
            if (candidates.Count > MaxTemperatureSensors)
                return Reading.Unavailable(TemperatureSource, "Too many CPU sensors for a bounded local sample.");
            foreach (var group in candidates.GroupBy(sensor => sensor.Rank))
            {
                var readings = new List<(TemperatureSensor Sensor, double Value)>();
                foreach (var sensor in group)
                    if (ParseCelsiusRawValue(sensor.RawValue) is double value) readings.Add((sensor, value));
                if (readings.Count > 0)
                    return BuildTemperatureReading(group.Key == 4, readings, group.Count(),
                        "Celsius RawValue from the discovery response. Provider acquisition timestamp is unavailable.");
            }
            return Reading.Unavailable(TemperatureSource, "No recognized CPU source with a valid Celsius RawValue.");
        }
        catch (JsonException) { return Reading.Unavailable(TemperatureSource, "Invalid local source JSON."); }
    }

    internal static double? ParseLibreHardwareMonitorSensorJson(string json)
    {
        try
        {
            if (Encoding.UTF8.GetByteCount(json) > MaxHttpBytes) return null;
            using var document = JsonDocument.Parse(json, JsonOptions);
            return ReadSensorResponse(document.RootElement);
        }
        catch (JsonException) { return null; }
    }

    private static double? ReadSensorResponse(JsonElement root)
    {
        // v0.9.6's GET success does not include "result"; its POST wrapper does.
        if (root.ValueKind != JsonValueKind.Object ||
            (root.TryGetProperty("result", out var result) && (result.ValueKind != JsonValueKind.String || result.GetString() != "ok")) ||
            GetString(root, "format") is not string format || !format.EndsWith("°C", StringComparison.Ordinal) ||
            !root.TryGetProperty("value", out var number) || number.ValueKind != JsonValueKind.Number ||
            !number.TryGetDouble(out var value)) return null;
        return IsValidTemperature(value) ? value : null;
    }

    private sealed record TemperatureSensor(string Id, string Name, string? RawValue, int Rank);
    private static List<TemperatureSensor> FindTemperatureSensors(JsonElement root)
    {
        var candidates = new Dictionary<string, TemperatureSensor>(StringComparer.OrdinalIgnoreCase);
        Visit(root, null, 0);
        return candidates.Values.OrderBy(sensor => sensor.Rank).ThenBy(sensor => sensor.Id, StringComparer.Ordinal).ToList();

        void Visit(JsonElement node, string? cpuParent, int depth)
        {
            if (depth > 60 || node.ValueKind != JsonValueKind.Object) return;
            var hardwareId = GetString(node, "HardwareId");
            if (hardwareId is not null) cpuParent = IsKnownCpuHardware(hardwareId) ? hardwareId : null;
            var id = GetString(node, "SensorId");
            var name = GetString(node, "Text");
            if (cpuParent is not null && id is not null && name is not null &&
                string.Equals(GetString(node, "Type"), "Temperature", StringComparison.OrdinalIgnoreCase) &&
                IsCpuTemperatureId(id, cpuParent) && TemperatureRank(name) is int rank)
                candidates.TryAdd(id, new(id, name, GetString(node, "RawValue"), rank));
            if (node.TryGetProperty("Children", out var children) && children.ValueKind == JsonValueKind.Array)
                foreach (var child in children.EnumerateArray()) Visit(child, cpuParent, depth + 1);
        }
    }

    private static bool IsKnownCpuHardware(string id)
    {
        var prefix = id.StartsWith("/intelcpu/", StringComparison.OrdinalIgnoreCase) ? "/intelcpu/"
            : id.StartsWith("/amdcpu/", StringComparison.OrdinalIgnoreCase) ? "/amdcpu/" : null;
        return prefix is not null && IsAsciiDigits(id.AsSpan(prefix.Length));
    }

    private static bool IsCpuTemperatureId(string id, string parent)
    {
        var prefix = parent + "/temperature/";
        return id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && IsAsciiDigits(id.AsSpan(prefix.Length));
    }

    private static int? TemperatureRank(string name)
    {
        if (name.Equals("CPU Package", StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.Equals("Core (Tdie)", StringComparison.OrdinalIgnoreCase) || name.Equals("CPU (Tdie)", StringComparison.OrdinalIgnoreCase)) return 1;
        if (name.Equals("Core (Tctl/Tdie)", StringComparison.OrdinalIgnoreCase) || name.Equals("CPU (Tctl/Tdie)", StringComparison.OrdinalIgnoreCase)) return 2;
        if (name.Equals("Core Average", StringComparison.OrdinalIgnoreCase)) return 3;
        if (name.Equals("CPU Core", StringComparison.OrdinalIgnoreCase)) return 4;
        foreach (var prefix in new[] { "CPU Core #", "P-Core #", "E-Core #" })
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && IsAsciiDigits(name.AsSpan(prefix.Length))) return 4;
        // Standalone Tctl may include a control offset; TjMax Distance is a delta, not CPU heat.
        return null;
    }

    private static bool IsAsciiDigits(ReadOnlySpan<char> value)
    {
        if (value.Length == 0) return false;
        foreach (var character in value) if (character is < '0' or > '9') return false;
        return true;
    }

    private static double? ParseCelsiusRawValue(string? raw)
    {
        if (raw is null) return null;
        raw = raw.Trim();
        if (!raw.EndsWith("°C", StringComparison.Ordinal)) return null;
        var number = raw[..^2].Trim();
        // LHM emits fixed-point F1, with the provider's decimal separator and no grouping.
        // Accept a single decimal comma or dot, never thousands separators or display Fahrenheit.
        if (number.Contains(',') && number.Contains('.')) return null;
        number = number.Replace(',', '.');
        if (!double.TryParse(number, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value)) return null;
        return IsValidTemperature(value) ? value : null;
    }

    private static bool IsValidTemperature(double value) => double.IsFinite(value) && value is >= -20 and <= 150;

    private static Reading BuildTemperatureReading(bool average, List<(TemperatureSensor Sensor, double Value)> readings,
        int selectedCount, string detail)
    {
        if (readings.Count == 0)
            return Reading.Unavailable(TemperatureSource, "No valid CPU temperature values; null/non-finite/outside -20..150 °C or unsupported sensor/unit rejected. " + detail);
        var value = average ? readings.Average(item => item.Value) : readings.Max(item => item.Value);
        var sources = string.Join("; ", readings.Select(item => $"{item.Sensor.Id} ({item.Sensor.Name})={item.Value.ToString("0.###", CultureInfo.InvariantCulture)} °C"));
        return Reading.Available(value, TemperatureSource,
            $"{(average ? "Mean of mapped CPU cores" : "Hottest recognized CPU source at the preferred label priority")}; {readings.Count}/{selectedCount} valid. {sources}. {detail}");
    }

    private static string? GetString(JsonElement node, string property) =>
        node.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool IsMissingNativeApi(Exception ex) => ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException;
    private static string ApiFailure(Exception ex) => $"Windows native API unavailable ({ex.GetType().Name}).";
    private static string NativeError(string operation) => $"{operation} failed (Win32 error {Marshal.GetLastWin32Error()}).";

    private void CloseGpuQuery()
    {
        var handle = gpuQuery;
        gpuQuery = gpuCounter = IntPtr.Zero;
        gpuPrimed = false;
        if (handle == IntPtr.Zero) return;
        try { PdhCloseQuery(handle); }
        catch (Exception ex) when (IsMissingNativeApi(ex)) { }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        CloseGpuQuery();
        localHttp?.Dispose();
        localHttp = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint Low;
        public uint High;
        public readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PdhFormattedValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhCounterItem
    {
        public IntPtr Name;
        public PdhFormattedValue Value;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out NativeFileTime idle, out NativeFileTime kernel, out NativeFileTime user);
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetActiveProcessorCount(ushort groupNumber);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PdhOpenQueryW(string? dataSource, UIntPtr userData, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, UIntPtr userData, out IntPtr counter);
    [DllImport("pdh.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bytes, out uint count, IntPtr items);
    [DllImport("pdh.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PdhCloseQuery(IntPtr query);
}

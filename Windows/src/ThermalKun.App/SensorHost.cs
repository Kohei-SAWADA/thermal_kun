using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using LibreHardwareMonitor.Hardware;

namespace ThermalKun.App;

internal static class SensorHost
{
    internal static int Run(string[] args)
    {
        if (args.Length != 4 || !int.TryParse(args[2], out var parentId) || !long.TryParse(args[3], out var parentTicks) ||
            !args[1].StartsWith($"thermal-kun-{parentId}-", StringComparison.Ordinal) || args[1].Length > 96) return 2;
        try
        {
            using var parent = Process.GetProcessById(parentId);
            if (parent.StartTime.ToUniversalTime().Ticks != parentTicks ||
                !string.Equals(parent.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return 3;
            using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.Out, PipeOptions.Asynchronous);
            pipe.Connect(15000);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverId) || serverId != parentId) return 4;
            using var identity = WindowsIdentity.GetCurrent();
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            {
                Write(pipe, null, "", "", "CPU温度の読み取りには管理者承認が必要です。"); return 5;
            }
            if (!BundledTemperature.DriverInstalled)
            {
                Write(pipe, null, "", "", "PawnIO 2.2以降が見つかりません。設定で温度を有効にしてください。"); return 6;
            }
            var computer = new Computer { IsCpuEnabled = true };
            try
            {
                computer.Open();
                while (!parent.HasExited && pipe.IsConnected)
                {
                    var candidates = new List<ISensor>();
                    foreach (var hardware in computer.Hardware.Where(h => h.HardwareType == HardwareType.Cpu))
                    {
                        hardware.Update();
                        candidates.AddRange(hardware.Sensors.Where(s => s.SensorType == SensorType.Temperature &&
                            s.Value is float value && float.IsFinite(value) && value is >= -20 and <= 150 &&
                            TemperatureSelection.Rank(s.Name) != int.MaxValue));
                    }
                    var rank = candidates.Count == 0 ? int.MaxValue : candidates.Min(s => TemperatureSelection.Rank(s.Name));
                    var selected = candidates.Where(s => TemperatureSelection.Rank(s.Name) == rank).OrderByDescending(s => s.Value).FirstOrDefault();
                    if (!Write(pipe, selected?.Value, selected?.Identifier.ToString() ?? "", selected?.Name ?? "",
                        selected is null ? "CPU温度を取得できませんでした。ドライバーまたはCPUの対応を確認してください。"
                        : "CPU package preferred; otherwise highest measured Tdie/core. CPU hardware only. No fan or clock controls are changed."))
                    {
                        StartupLog.Write("CPU reader stopped: GUI pipe disconnected");
                        break;
                    }
                    Thread.Sleep(1000);
                }
            }
            catch (Exception ex) { StartupLog.Error("CPU reader failed", ex); Write(pipe, null, "", "", "CPU温度取得エラー: " + ex.Message); return 7; }
            finally { computer.Close(); }
            StartupLog.Write("CPU reader exited normally"); return 0;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        { StartupLog.Error("CPU reader connection ended", ex); return 8; }
    }

    private static bool Write(Stream output, double? value, string id, string name, string detail) =>
        SensorPipeWriter.TryWrite(output, new CpuTemperatureFrame(DateTimeOffset.UtcNow, value, id, name, detail));

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);
}

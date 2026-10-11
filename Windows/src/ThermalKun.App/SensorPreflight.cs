using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace ThermalKun.App;

// Explicit diagnostic: test the real bundled helper/pipe without requesting elevation or installing anything.
internal static class SensorPreflight
{
    internal static async Task<int> Run(string[] args)
    {
        var outputIndex = Array.IndexOf(args, "--output");
        if (outputIndex < 0 || outputIndex + 1 >= args.Length) return 2;
        var path = args[outputIndex + 1];
        try
        {
            using var self = Process.GetCurrentProcess();
            using var identity = WindowsIdentity.GetCurrent();
            var elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            var name = $"thermal-kun-{Environment.ProcessId}-{Guid.NewGuid():N}";
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var pipe = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var host = Process.Start(new ProcessStartInfo(Environment.ProcessPath!,
                $"--sensor-host {name} {Environment.ProcessId} {self.StartTime.ToUniversalTime().Ticks}")
                { UseShellExecute = false, CreateNoWindow = true }) ?? throw new IOException("Host did not start.");
            await pipe.WaitForConnectionAsync(deadline.Token);
            using var reader = new StreamReader(pipe);
            var line = await reader.ReadLineAsync(deadline.Token);
            var frame = JsonSerializer.Deserialize<CpuTemperatureFrame>(line ?? "null") ?? throw new IOException("Missing frame.");
            var reading = TemperatureSelection.ValidateFrame(frame, DateTimeOffset.UtcNow);
            pipe.Close();
            await host.WaitForExitAsync(deadline.Token);
            var pass = elevated || (!reading.IsAvailable && host.ExitCode == 5);
            File.WriteAllText(path, JsonSerializer.Serialize(new { pass, elevated, driverInstalled = BundledTemperature.DriverInstalled,
                hostExitCode = host.ExitCode, frame, reading, scope = "No elevation prompt, no installation. Ordinary-permission host must not emit a CPU temperature." },
                new JsonSerializerOptions { WriteIndented = true }));
            return pass ? 0 : 1;
        }
        catch (Exception ex)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new { pass = false, error = ex.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            return 1;
        }
    }
}

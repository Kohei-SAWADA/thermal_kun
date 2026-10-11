using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using ThermalKun.Core;

namespace ThermalKun.App;

// A private pipe carries measurements only. The normal WPF process never opens the hardware driver.
internal sealed class BundledTemperature : IDisposable
{
    internal const string Source = "LibreHardwareMonitor 0.9.6 · bundled CPU reader";
    internal const string InstallerHash = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";
    private readonly object gate = new();
    private CancellationTokenSource? session;
    private Task? reader;
    private Reading latest = Reading.Unavailable(Source, "Settings → CPU温度を有効にする。初回はPawnIOの導入が必要です。");
    private DateTimeOffset lastReceived;
    private string status = "CPU温度は未接続です。「CPU温度を有効にする」を選んでください。";
    private bool enabling;
    private bool disposed;

    internal string Status { get { lock (gate) return status; } }
    internal static bool DriverInstalled
    {
        get
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            return Version.TryParse(key?.GetValue("DisplayVersion") as string, out var version) && version >= new Version(2, 2);
        }
    }

    internal Reading Read()
    {
        lock (gate)
        {
            if (latest.IsAvailable && DateTimeOffset.UtcNow - lastReceived > TimeSpan.FromSeconds(6))
                return Reading.Unavailable(Source, "温度取得が6秒以上途切れました。設定で再接続してください。");
            return latest;
        }
    }

    internal async Task<bool> EnableAsync(bool allowInstall)
    {
        lock (gate)
        {
            if (disposed || enabling) return false;
            if (session is not null && reader is { IsCompleted: false } && latest.IsAvailable) return true;
            enabling = true;
        }
        try
        {
            if (!DriverInstalled)
            {
                if (!allowInstall) { SetStatus("PawnIOが未導入です。設定でCPU温度を有効にしてください。"); return false; }
                var installer = Path.Combine(AppContext.BaseDirectory, "drivers", "PawnIO_setup.exe");
                if (!File.Exists(installer)) throw new IOException("同梱のPawnIO_setup.exeが見つかりません。ZIPをすべて展開してください。");
                using (var file = File.OpenRead(installer))
                    if (!Convert.ToHexString(SHA256.HashData(file)).Equals(InstallerHash, StringComparison.Ordinal))
                        throw new IOException("PawnIOインストーラーの検証に失敗しました。実行を中止しました。");
                SetStatus("PawnIOの導入を待っています。Windowsの確認画面を操作してください。");
                using var install = Process.Start(new ProcessStartInfo(installer, "-install -silent")
                { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(installer)! });
                if (install is null) throw new IOException("PawnIOを起動できませんでした。");
                await install.WaitForExitAsync();
                StartupLog.Write($"PawnIO installer exited; code={install.ExitCode}");
                if (install.ExitCode == 3010) { SetStatus("PawnIOの導入は完了しましたが、Windowsは再起動が必要と報告しました。自動再起動は行いません。"); return false; }
                if (install.ExitCode is not (0 or 183) || !DriverInstalled)
                    throw new IOException($"PawnIOを確認できませんでした（終了コード {install.ExitCode}）。");
            }
            lock (gate) if (disposed) return false;
            Stop();
            var cancellation = new CancellationTokenSource();
            var pipeName = $"thermal-kun-{Environment.ProcessId}-{Guid.NewGuid():N}";
            var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            lock (gate) session = cancellation;
            reader = ReceiveAsync(pipe, cancellation.Token);
            using var self = Process.GetCurrentProcess();
            var arguments = $"--sensor-host {pipeName} {Environment.ProcessId} {self.StartTime.ToUniversalTime().Ticks}";
            SetStatus("温度取得用の管理者承認を待っています。表示画面は通常権限のままです。");
            using var host = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, arguments)
            { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory });
            if (host is null) throw new IOException("温度取得プロセスを起動できませんでした。");
            StartupLog.Write($"Bundled sensor host requested; pid={host.Id}");
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Stop(); SetStatus("管理者承認がキャンセルされました。CPU負荷・メモリ表示は引き続き使えます。"); return false;
        }
        catch (Exception ex)
        {
            Stop(); SetStatus("CPU温度に接続できません: " + ex.Message); StartupLog.Error("Bundled temperature start failed", ex); return false;
        }
        finally { lock (gate) enabling = false; }
    }

    private async Task ReceiveAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        try
        {
            using (pipe)
            using (var connect = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                connect.CancelAfter(TimeSpan.FromSeconds(90));
                await pipe.WaitForConnectionAsync(connect.Token);
                using var input = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
                while (!token.IsCancellationRequested)
                {
                    // Bound every frame; a stopped or malformed host must not leave a stale number on screen.
                    var line = new StringBuilder();
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(10));
                    var character = new char[1];
                    while (await input.ReadAsync(character.AsMemory(), deadline.Token) == 1 && character[0] != '\n')
                    {
                        if (line.Length >= 16384) throw new IOException("Sensor frame is too large.");
                        line.Append(character[0]);
                    }
                    if (line.Length == 0) break;
                    var frame = JsonSerializer.Deserialize<CpuTemperatureFrame>(line.ToString()) ?? throw new IOException("Empty sensor frame.");
                    var reading = TemperatureSelection.ValidateFrame(frame, DateTimeOffset.UtcNow);
                    lock (gate)
                    {
                        if (session?.Token != token) return;
                        latest = reading; lastReceived = DateTimeOffset.UtcNow;
                        status = reading.IsAvailable ? $"CPU温度を取得中: {reading.Value:0.0} °C · {frame.SensorName}" : reading.Detail;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException or UnauthorizedAccessException)
        {
            if (!token.IsCancellationRequested)
            {
                lock (gate)
                    if (session?.Token == token) { status = "温度取得との接続が終了しました: " + ex.Message; latest = Reading.Unavailable(Source, status); }
                StartupLog.Error("Sensor pipe ended", ex);
            }
        }
        finally
        {
            lock (gate)
                if (session?.Token == token)
                {
                    if (latest.IsAvailable) status = "温度取得との接続が終了しました。設定で再接続してください。";
                    latest = Reading.Unavailable(Source, status);
                }
        }
    }

    private void SetStatus(string text)
    {
        lock (gate) { status = text; latest = Reading.Unavailable(Source, text); }
    }
    internal void Stop()
    {
        CancellationTokenSource? old;
        lock (gate) { old = session; session = null; latest = Reading.Unavailable(Source, "CPU温度は停止中です。設定で再接続できます。"); status = latest.Detail; }
        old?.Cancel();
        // Let the pipe close normally. No process is forcefully terminated.
    }
    public void Dispose() { lock (gate) disposed = true; Stop(); }
}

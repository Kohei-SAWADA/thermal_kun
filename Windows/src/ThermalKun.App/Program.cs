using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using ThermalKun.Core;
using Forms = System.Windows.Forms;

namespace ThermalKun.App;
public static class Program
{
    private const string ShowRequestName = @"Local\ThermalKun.Windows.Temperature.Show";

    [STAThread]
    public static int Main(string[] args)
    {
        StartupLog.Write($"Start Windows temperature preview; pid={Environment.ProcessId}; executable={Environment.ProcessPath}; " +
            $"arguments={JsonSerializer.Serialize(args)}; apartment={Thread.CurrentThread.GetApartmentState()}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => StartupLog.Write($"Unhandled exception: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) => StartupLog.Error("Unobserved task exception", e.Exception);
        try
        {
            if (args.Contains("--sensor-host")) return SensorHost.Run(args);
            if (args.Contains("--sensor-preflight")) return SensorPreflight.Run(args).GetAwaiter().GetResult();
            if (args.Contains("--diagnose")) return Diagnose(args);
            // Create the event before the mutex so simultaneous launches cannot miss it.
            using var showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, ShowRequestName);
            using var single = new Mutex(true, @"Local\ThermalKun.Windows.Temperature", out var first);
            StartupLog.Write($"Single instance: first={first}");
            if (!first)
            {
                AllowExistingInstanceForeground();
                showRequest.Set();
                StartupLog.Write("Requested existing panel; secondary instance exits");
                return 0;
            }
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (_, e) => StartupLog.Error("UI exception", e.Exception);
            using var controller = new AppController(app);
            controller.Start(showRequest);
            StartupLog.Write("Entering WPF dispatcher");
            var result = app.Run();
            StartupLog.Write($"WPF dispatcher exited; code={result}");
            return result;
        }
        catch (Exception ex)
        {
            StartupLog.Error("Startup failed", ex);
            MessageBox.Show($"thermal kun could not start.\n\n{ex.Message}\n\nLog: {StartupLog.FilePath}",
                "thermal kun", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }
    private static void AllowExistingInstanceForeground()
    {
        using var self = Process.GetCurrentProcess();
        foreach (var peer in Process.GetProcessesByName(self.ProcessName))
        {
            using (peer)
            {
                try
                {
                    if (peer.Id == self.Id || peer.SessionId != self.SessionId ||
                        !string.Equals(ReadProcessPath(peer.Id), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) continue;
                    StartupLog.Write($"Foreground permission for existing pid={peer.Id}: {AllowSetForegroundWindow((uint)peer.Id)}");
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                { StartupLog.Error("Could not grant existing instance foreground permission", ex); }
            }
        }
    }

    private static string? ReadProcessPath(int processId)
    {
        // Limited-query access also works for our elevated CPU helper; module enumeration does not.
        using var handle = OpenProcess(0x1000, false, (uint)processId);
        if (handle.IsInvalid) return null;
        var path = new System.Text.StringBuilder(32768);
        var length = path.Capacity;
        return QueryFullProcessImageName(handle, 0, path, ref length) ? path.ToString() : null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, System.Text.StringBuilder path, ref int size);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);

    private static int Diagnose(string[] args)
    {
        try
        {
            // Diagnostic mode never creates registry entries or changes preferences.
            var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ThermalKun", "temperature-settings.json");
            var prefs = new PreferencesStore(path).Load();
            using var sensors = new NativeSensors();
            sensors.Sample(prefs.LibreHardwareMonitorEnabled);
            Thread.Sleep(2000);
            var sample = sensors.Sample(prefs.LibreHardwareMonitorEnabled);
            var json = JsonSerializer.Serialize(new { version = "0.1.3-temperature-preview", bundledDriverInstalled = BundledTemperature.DriverInstalled, os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(), sourceTimestampMeaning = "Local collection/response time; external sensor acquisition age is not supplied by LibreHardwareMonitor.", sample },
                new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } });
            var index = Array.IndexOf(args, "--output");
            if (index >= 0 && index + 1 < args.Length) File.WriteAllText(args[index + 1], json);
            else { AttachConsole(uint.MaxValue); Console.WriteLine(json); }
            return 0;
        }
        catch (Exception ex)
        {
            StartupLog.Error("Diagnostics failed", ex);
            var index = Array.IndexOf(args, "--output");
            var error = JsonSerializer.Serialize(new { error = ex.Message });
            if (index >= 0 && index + 1 < args.Length) File.WriteAllText(args[index + 1], error);
            return 1;
        }
    }
    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern bool AttachConsole(uint processId);
}

internal sealed class AppController : IDisposable
{
    private readonly Application app;
    private readonly PreferencesStore store;
    private readonly StartupService startup = new();
    private readonly BundledTemperature temperature = new();
    private volatile Preferences preferences;
    private Snapshot current = Snapshot.Empty(DateTimeOffset.Now);
    private readonly TemperatureHistory history = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly CancellationTokenSource stop = new();
    private readonly PanelWindow panel;
    private readonly Forms.NotifyIcon tray;
    private SettingsWindow? settings;
    private Thread? worker;
    private RegisteredWaitHandle? showWait;
    private int resetRequested;
    private volatile bool sleeping;
    public AppController(Application app)
    {
        this.app = app;
        // Keep preview preferences separate while preserving 0.1.0 and fix 1.
        store = new(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ThermalKun", "temperature-settings.json"));
        preferences = store.Load();
        preferences = preferences with { LibreHardwareMonitorEnabled = false };
        current = current with { CpuTemperature = temperature.Read() };
        StartupLog.Write($"Preferences loaded; compact={preferences.Compact}; appearance={preferences.Appearance}; " +
            $"interval={preferences.UpdateInterval}; temperatureSourceEnabled={preferences.LibreHardwareMonitorEnabled}");
        panel = new(preferences);
        app.MainWindow = panel;
        panel.SourceInitialized += (_, _) => StartupLog.Write($"Panel HWND={new System.Windows.Interop.WindowInteropHelper(panel).Handle}");
        panel.ContentRendered += (_, _) => StartupLog.Write($"Panel rendered; visible={panel.IsVisible}; active={panel.IsActive}; " +
            $"frame={panel.Left},{panel.Top},{panel.ActualWidth},{panel.ActualHeight}");
        panel.ToggleCompactRequested += ToggleCompact;
        panel.SettingsRequested += OpenSettings;
        panel.HideRequested += HidePanel;
        panel.FrameChanged += SaveFrame;
        panel.Closing += (_, e) => { if (!stop.IsCancellationRequested) { e.Cancel = true; HidePanel(); } };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => app.Dispatcher.Invoke(() => ShowPanel("tray menu")));
        menu.Items.Add("Hide", null, (_, _) => app.Dispatcher.Invoke(HidePanel));
        menu.Items.Add("Settings…", null, (_, _) => app.Dispatcher.Invoke(OpenSettings));
        menu.Items.Add("Center Panel on Screen", null, (_, _) => app.Dispatcher.Invoke(panel.CenterOnScreen));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit thermal kun", null, (_, _) => app.Dispatcher.Invoke(Quit));
        tray = new() { Text = "thermal kun · Windows preview", ContextMenuStrip = menu, Visible = true,
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application };
        tray.DoubleClick += (_, _) => app.Dispatcher.Invoke(() => ShowPanel("tray double click"));
        StartupLog.Write("Tray icon created");
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.UserPreferenceChanged += PreferenceChanged;
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
    }
    public void Start(EventWaitHandle showRequest)
    {
        showWait = ThreadPool.RegisterWaitForSingleObject(showRequest, (_, _) =>
        {
            if (!stop.IsCancellationRequested && !app.Dispatcher.HasShutdownStarted)
                app.Dispatcher.BeginInvoke(() => ShowPanel("second launch"));
        }, null, Timeout.Infinite, false);
        panel.RestoreFrame(preferences.Compact ? preferences.CompactFrame : preferences.DetailFrame);
        ShowPanel("startup");
        worker = new Thread(SampleLoop) { Name = "thermal kun measurements", IsBackground = true, Priority = ThreadPriority.BelowNormal };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        StartupLog.Write("Measurement worker started");
        if (preferences.BundledTemperatureEnabled)
            app.Dispatcher.BeginInvoke(async () => await temperature.EnableAsync(false));
    }
    private void ShowPanel(string reason)
    {
        if (stop.IsCancellationRequested) return;
        panel.RestoreFrame(new(panel.Left, panel.Top, panel.Width));
        if (panel.WindowState == WindowState.Minimized) panel.WindowState = WindowState.Normal;
        panel.Show();
        var activated = panel.Activate();
        StartupLog.Write($"Show panel ({reason}); visible={panel.IsVisible}; activated={activated}; " +
            $"frame={panel.Left},{panel.Top},{panel.Width},{panel.Height}");
    }
    private void HidePanel()
    {
        panel.Hide();
        StartupLog.Write("Panel hidden");
    }
    private void SampleLoop()
    {
        using var native = new NativeSensors(temperature.Read);
        var firstSample = true;
        while (!stop.IsCancellationRequested)
        {
            var cycleStart = Stopwatch.GetTimestamp();
            if (!sleeping)
            {
                if (Interlocked.Exchange(ref resetRequested, 0) != 0) native.Reset();
                Snapshot sample;
                try { sample = native.Sample(preferences.LibreHardwareMonitorEnabled); }
                catch (Exception ex)
                {
                    StartupLog.Error("Measurement failed", ex);
                    sample = Snapshot.Empty(DateTimeOffset.Now) with { CpuLoad = Reading.Unavailable("Windows", ex.Message) };
                }
                if (!stop.IsCancellationRequested && !sleeping)
                {
                    app.Dispatcher.BeginInvoke(() =>
                    {
                        if (stop.IsCancellationRequested) return;
                        if (sample.SampledAt - current.SampledAt > TimeSpan.FromSeconds(Math.Max(6, preferences.UpdateInterval * 3))) history.MarkGap(sample.SampledAt.AddMilliseconds(-1));
                        current = sample; history.Add(sample); panel.UpdateSnapshot(sample, history); settings?.RefreshSources();
                        if (firstSample)
                        {
                            firstSample = false;
                            StartupLog.Write($"First sample displayed; timestamp={sample.SampledAt:O}; " +
                                $"cpu={sample.CpuLoad.State}; gpu={sample.GpuLoad.State}; memory={sample.MemoryUsedGiB.State}");
                        }
                    }, DispatcherPriority.Background);
                }
            }
            wake.WaitOne(TimeSpan.FromSeconds(Math.Max(0, preferences.UpdateInterval - Stopwatch.GetElapsedTime(cycleStart).TotalSeconds)));
        }
    }
    private void ToggleCompact() => Apply(preferences with { Compact = !preferences.Compact });
    private void SaveFrame(PanelFrame frame)
    {
        preferences = preferences.Compact ? preferences with { CompactFrame = frame } : preferences with { DetailFrame = frame };
        Persist(); settings?.ApplyPreferences(preferences);
    }
    private void Apply(Preferences next)
    {
        var old = preferences;
        var modeChanged = next.Compact != old.Compact;
        var currentFrame = new PanelFrame(panel.Left, panel.Top, panel.Width);
        if (modeChanged)
            old = old.Compact ? old with { CompactFrame = currentFrame } : old with { DetailFrame = currentFrame };
        // Frame values belong to the panel and can change while Settings is open.
        preferences = next.Normalize() with { CompactFrame = old.CompactFrame, DetailFrame = old.DetailFrame };
        panel.ApplyPreferences(preferences);
        if (modeChanged)
        {
            var desired = preferences.Compact ? preferences.CompactFrame : preferences.DetailFrame;
            panel.RestoreFrame(new(currentFrame.X, currentFrame.Y, desired?.Size ?? (preferences.Compact ? 360 : 480)));
            SaveFrame(new(panel.Left, panel.Top, panel.Width));
        }
        else Persist();
        settings?.ApplyPreferences(preferences);
        if (old.UpdateInterval != preferences.UpdateInterval || old.LibreHardwareMonitorEnabled != preferences.LibreHardwareMonitorEnabled) wake.Set();
    }
    private void Persist()
    {
        try { store.Save(preferences); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { tray.Text = "thermal kun · settings could not be saved"; }
    }
    private void OpenSettings()
    {
        if (settings is null)
        {
            settings = new(preferences, () => current, startup.Status, enabled => startup.Set(enabled), Apply, panel.CenterOnScreen,
                () => temperature.Status, async () =>
                {
                    if (!BundledTemperature.DriverInstalled && MessageBox.Show(
                        "CPU温度を取得するため、同梱の署名済みPawnIO 2.2.0ドライバーを導入します。\n\n" +
                        "初回はドライバー導入、その後は温度取得用プロセスにWindowsの管理者承認が必要です。" +
                        "表示画面は通常権限で動きます。自動起動の登録や自動再起動は行いません。\n\n導入して接続しますか？",
                        "CPU温度を有効にする", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
                    if (await temperature.EnableAsync(true))
                        Apply(preferences with { BundledTemperatureEnabled = true, LibreHardwareMonitorEnabled = false });
                }, () => { temperature.Stop(); Apply(preferences with { BundledTemperatureEnabled = false }); }, Quit);
            settings.Owner = panel;
            settings.Closed += (_, _) => settings = null;
        }
        settings.Show(); settings.Activate();
    }
    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    {
        sleeping = e.Mode == PowerModes.Suspend;
        if (e.Mode == PowerModes.Resume) { Interlocked.Exchange(ref resetRequested, 1); wake.Set(); }
    }
    private void PreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => app.Dispatcher.BeginInvoke(() => panel.ApplyPreferences(preferences));
    private void DisplayChanged(object? sender, EventArgs e) => app.Dispatcher.BeginInvoke(() =>
    {
        panel.RestoreFrame(new(panel.Left, panel.Top, panel.Width)); SaveFrame(new(panel.Left, panel.Top, panel.Width));
    });
    private void Quit() { StartupLog.Write("Quit requested"); stop.Cancel(); wake.Set(); panel.Close(); settings?.Close(); app.Shutdown(); }
    public void Dispose()
    {
        showWait?.Unregister(null);
        temperature.Dispose();
        stop.Cancel(); wake.Set(); worker?.Join(2500); tray.Dispose();
        SystemEvents.PowerModeChanged -= PowerChanged; SystemEvents.UserPreferenceChanged -= PreferenceChanged; SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        if (worker is null || !worker.IsAlive) { wake.Dispose(); stop.Dispose(); }
        StartupLog.Write("Controller disposed");
    }
}

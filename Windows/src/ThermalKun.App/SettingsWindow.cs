using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ThermalKun.Core;

namespace ThermalKun.App;

public sealed class SettingsWindow : Window
{
    private Preferences _preferences;
    private readonly Func<Snapshot> _current;
    private readonly Func<(bool Enabled, string Detail)> _startupStatus;
    private readonly Action<bool> _changeStartup;
    private readonly Action<Preferences> _apply;
    private readonly Action _center;
    private readonly Func<string> _temperatureStatus;
    private readonly Func<Task> _enableTemperature;
    private readonly Action _stopTemperature;
    private readonly Action _quit;
    private TextBlock _bundledStatus = null!;
    private InstrumentTheme _theme;
    private readonly Dictionary<Appearance, RadioButton> _appearance = new();
    private readonly Dictionary<int, RadioButton> _intervals = new();
    private CheckBox _compact = null!, _topmost = null!, _animations = null!, _startup = null!;
    private TextBlock _startupDetail = null!, _startupError = null!, _preferenceError = null!, _motionDetail = null!;
    private TextBox _sources = null!;
    private readonly DispatcherTimer _refreshTimer;
    private bool _updating;
    private bool _lastHighContrast;

    public SettingsWindow(Preferences preferences, Func<Snapshot> current,
        Func<(bool Enabled, string Detail)> startupStatus, Action<bool> changeStartup,
        Action<Preferences> apply, Action center, Func<string> temperatureStatus, Func<Task> enableTemperature, Action stopTemperature, Action quit)
    {
        _preferences = preferences.Normalize();
        _current = current;
        _startupStatus = startupStatus;
        _changeStartup = changeStartup;
        _apply = apply;
        _center = center;
        _temperatureStatus = temperatureStatus;
        _enableTemperature = enableTemperature;
        _stopTemperature = stopTemperature;
        _quit = quit;
        _theme = InstrumentTheme.For(_preferences.Appearance);
        _lastHighContrast = SystemParameters.HighContrast;
        Title = "thermal kun · Settings";
        Width = 540;
        Height = 760;
        MinWidth = 480;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        _theme.Apply(this);
        Content = BuildContent();
        ApplyPreferences(_preferences);
        RefreshStartupStatus();
        RefreshSources();
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _refreshTimer.Tick += (_, _) =>
        {
            RefreshTheme();
            RefreshStartupStatus();
            UpdateMotionDetail();
            _bundledStatus.Text = _temperatureStatus();
            // Preserve selection while the user is inspecting or copying source details.
            if (!_sources.IsKeyboardFocusWithin) RefreshSources();
        };
        Loaded += (_, _) => _refreshTimer.Start();
        Closed += (_, _) => _refreshTimer.Stop();
        Activated += (_, _) => { RefreshStartupStatus(); RefreshTheme(); };
    }

    public void ApplyPreferences(Preferences preferences)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => ApplyPreferences(preferences)); return; }
        _updating = true;
        try
        {
            _preferences = preferences.Normalize();
            foreach (var (appearance, radio) in _appearance) radio.IsChecked = appearance == _preferences.Appearance;
            foreach (var (interval, radio) in _intervals) radio.IsChecked = interval == _preferences.UpdateInterval;
            _compact.IsChecked = _preferences.Compact;
            _topmost.IsChecked = _preferences.AlwaysOnTop;
            _animations.IsChecked = _preferences.Animations;
            RefreshTheme();
            UpdateMotionDetail();
        }
        finally { _updating = false; }
    }

    public void RefreshSources() => RefreshSources(false);

    private void RefreshSources(bool force)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => RefreshSources(force)); return; }
        if (!force && _sources.IsKeyboardFocusWithin) return;
        try
        {
            var sample = _current();
            var text = new StringBuilder();
            text.AppendLine("APP SAMPLE  " + sample.SampledAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
            text.AppendLine(sample.IsStale(DateTimeOffset.UtcNow, _preferences.UpdateInterval) ? "STALE · sampling is paused or delayed." : "Last completed app sampling cycle.");
            text.AppendLine("Bundled CPU readings include acquisition time; values older than 6 seconds are rejected.");
            foreach (var (name, reading, unit) in new[]
            {
                ("CPU TEMPERATURE", sample.CpuTemperature, "°C"),
                ("CPU LOAD", sample.CpuLoad, "%"),
                ("GPU LOAD", sample.GpuLoad, "%"),
                ("MEMORY USED", sample.MemoryUsedGiB, " GiB"),
                ("MEMORY TOTAL", sample.MemoryTotalGiB, " GiB")
            })
            {
                text.AppendLine();
                text.AppendLine(name + "  " + (reading.IsAvailable ? reading.Value!.Value.ToString("0.0", CultureInfo.InvariantCulture) + unit : reading.MissingLabel));
                text.AppendLine("Source: " + reading.Source);
                text.AppendLine("State: " + reading.State);
                text.AppendLine(reading.Detail);
            }
            var output = text.ToString().TrimEnd();
            if (_sources.Text != output) _sources.Text = output;
        }
        catch (Exception ex)
        {
            _sources.Text = "Source details could not be read.\n" + ex.Message;
        }
    }

    private ScrollViewer BuildContent()
    {
        var body = new StackPanel { Margin = new Thickness(22, 18, 22, 22) };
        var title = InstrumentTheme.Label("thermal kun", 21, InstrumentTheme.SupportKey, true);
        body.Children.Add(title);
        body.Children.Add(BodyText("Measured system telemetry. Changes apply immediately.", new Thickness(0, 3, 0, 16)));

        var bundled = Group("CPU TEMPERATURE · 同梱センサー");
        bundled.Children.Add(BodyText("別アプリやWebサーバーの設定は不要です。初回は同梱のPawnIOドライバーを導入します。温度取得時はWindowsの管理者承認が必要です（アプリを再起動したときも確認が出ます）。"));
        _bundledStatus = BodyText(_temperatureStatus(), new Thickness(0, 9, 0, 9));
        AutomationProperties.SetName(_bundledStatus, "CPU temperature connection status");
        bundled.Children.Add(_bundledStatus);
        var connect = new Button { Content = "CPU温度を有効にする / 再接続", HorizontalAlignment = HorizontalAlignment.Left };
        var disconnect = new Button { Content = "温度取得を停止", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        connect.Click += async (_, _) =>
        {
            connect.IsEnabled = false;
            disconnect.IsEnabled = false;
            try { await _enableTemperature(); }
            finally { connect.IsEnabled = true; disconnect.IsEnabled = true; _bundledStatus.Text = _temperatureStatus(); }
        };
        bundled.Children.Add(connect);
        disconnect.Click += (_, _) => { _stopTemperature(); _bundledStatus.Text = _temperatureStatus(); };
        bundled.Children.Add(disconnect);
        var quit = new Button { Content = "thermal kunを終了", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        quit.Click += (_, _) => _quit();
        bundled.Children.Add(quit);
        bundled.Children.Add(BodyText("ドライバーはPCに残ります。削除する場合はWindowsの「インストールされているアプリ」でPawnIOをアンインストールしてください。", new Thickness(0, 8, 0, 0)));
        body.Children.Add(WrapGroup(bundled));

        var appearance = Group("PANEL");
        appearance.Children.Add(BodyText("Appearance", new Thickness(0, 0, 0, 6)));
        var choices = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var option in new[] { Appearance.Dark, Appearance.Light, Appearance.System })
        {
            var radio = new RadioButton { Content = option.ToString(), GroupName = "Appearance", Margin = new Thickness(0, 0, 22, 0) };
            _appearance.Add(option, radio);
            radio.Checked += (_, _) => ChangePreferences(p => p with { Appearance = option });
            choices.Children.Add(radio);
        }
        appearance.Children.Add(choices);
        _topmost = PreferenceCheck("Always on top", p => p with { AlwaysOnTop = _topmost.IsChecked == true });
        _topmost.Margin = new Thickness(0, 14, 0, 8);
        appearance.Children.Add(_topmost);
        _compact = PreferenceCheck("Compact view", p => p with { Compact = _compact.IsChecked == true });
        _compact.Margin = new Thickness(0, 0, 0, 8);
        appearance.Children.Add(_compact);
        appearance.Children.Add(BodyText("Compact view keeps CPU temperature, CPU load and memory. Expand to see GPU load, history and the temperature peak."));
        _animations = PreferenceCheck("Enable animations", p => p with { Animations = _animations.IsChecked == true });
        _animations.Margin = new Thickness(0, 12, 0, 5);
        appearance.Children.Add(_animations);
        _motionDetail = BodyText("");
        appearance.Children.Add(_motionDetail);
        var center = new Button { Content = "Center Panel", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 13, 0, 0) };
        center.Click += (_, _) => _center();
        appearance.Children.Add(center);
        body.Children.Add(WrapGroup(appearance));

        var sampling = Group("SAMPLING");
        sampling.Children.Add(BodyText("Update interval", new Thickness(0, 0, 0, 7)));
        var intervals = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var seconds in new[] { 1, 2, 5, 10 })
        {
            var radio = new RadioButton { Content = seconds + " sec", GroupName = "Interval", Margin = new Thickness(0, 0, 18, 0) };
            _intervals.Add(seconds, radio);
            radio.Checked += (_, _) => ChangePreferences(p => p with { UpdateInterval = seconds });
            intervals.Children.Add(radio);
        }
        sampling.Children.Add(intervals);
        sampling.Children.Add(BodyText("Default: 2 seconds. CPU and GPU counters need two samples after starting. Stale readings are marked after the greater of 6 seconds or three update intervals.", new Thickness(0, 8, 0, 0)));
        body.Children.Add(WrapGroup(sampling));

        var login = Group("WINDOWS SIGN-IN");
        _startup = new CheckBox { Content = "Start thermal kun when I sign in", IsThreeState = false };
        _startup.Checked += (_, _) => ChangeStartup(true);
        _startup.Unchecked += (_, _) => ChangeStartup(false);
        login.Children.Add(_startup);
        login.Children.Add(BodyText("Opt in explicitly. This changes this app's per-user startup entry. The checkbox and status below are read back from Windows.", new Thickness(0, 7, 0, 0)));
        _startupDetail = BodyText("", new Thickness(0, 7, 0, 0));
        login.Children.Add(_startupDetail);
        _startupError = BodyText("", new Thickness(0, 5, 0, 0));
        _startupError.SetResourceReference(TextBlock.ForegroundProperty, InstrumentTheme.AmberKey);
        _startupError.Visibility = Visibility.Collapsed;
        login.Children.Add(_startupError);
        var removeStartup = new Button
        {
            Content = "Remove startup registration", HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 10, 0, 0),
            ToolTip = "Remove only thermal kun's startup registration for your current Windows account. This also works when Windows has disabled the registered entry."
        };
        removeStartup.Click += (_, _) => ChangeStartup(false);
        login.Children.Add(removeStartup);
        login.Children.Add(BodyText("Remove deletes only this app's current-user Run entry, including an entry that Windows has disabled. It leaves Windows startup approval settings unchanged.", new Thickness(0, 7, 0, 0)));
        body.Children.Add(WrapGroup(login));

        var sources = Group("CURRENT SENSOR SOURCES");
        sources.Children.Add(BodyText("Select and copy the details below for troubleshooting. Refresh pauses while this text has focus."));
        _sources = new TextBox
        {
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            FontFamily = new FontFamily("Consolas"), FontSize = 11,
            Height = 230, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(8), Margin = new Thickness(0, 8, 0, 8), BorderThickness = new Thickness(1)
        };
        _sources.SetResourceReference(Control.BackgroundProperty, InstrumentTheme.BackgroundKey);
        _sources.SetResourceReference(Control.ForegroundProperty, InstrumentTheme.TextKey);
        _sources.SetResourceReference(Control.BorderBrushProperty, InstrumentTheme.RuleKey);
        AutomationProperties.SetName(_sources, "Current sensor sources and reading details");
        sources.Children.Add(_sources);
        var refresh = new Button { Content = "Refresh Sources", HorizontalAlignment = HorizontalAlignment.Left };
        refresh.Click += (_, _) => RefreshSources(true);
        sources.Children.Add(refresh);
        body.Children.Add(WrapGroup(sources));
        _preferenceError = BodyText("", new Thickness(0, 4, 0, 10));
        _preferenceError.SetResourceReference(TextBlock.ForegroundProperty, InstrumentTheme.AmberKey);
        _preferenceError.Visibility = Visibility.Collapsed;
        body.Children.Add(_preferenceError);
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 78, IsCancel = true };
        close.Click += (_, _) => Close();
        body.Children.Add(close);
        return new ScrollViewer
        {
            Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    private void ChangePreferences(Func<Preferences, Preferences> update)
    {
        if (_updating) return;
        var previous = _preferences;
        try
        {
            _preferences = update(_preferences).Normalize();
            _apply(_preferences);
            _preferenceError.Visibility = Visibility.Collapsed;
            RefreshTheme();
            UpdateMotionDetail();
            RefreshSources();
        }
        catch (Exception ex)
        {
            ApplyPreferences(previous);
            _preferenceError.Text = "Settings could not be applied: " + ex.Message;
            _preferenceError.Visibility = Visibility.Visible;
        }
    }

    private CheckBox PreferenceCheck(string label, Func<Preferences, Preferences> update)
    {
        var checkbox = new CheckBox { Content = label, IsThreeState = false };
        checkbox.Checked += (_, _) => ChangePreferences(update);
        checkbox.Unchecked += (_, _) => ChangePreferences(update);
        return checkbox;
    }

    private void ChangeStartup(bool enabled)
    {
        if (_updating) return;
        try
        {
            _changeStartup(enabled);
            _startupError.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _startupError.Text = "Windows startup could not be changed: " + ex.Message;
            _startupError.Visibility = Visibility.Visible;
        }
        RefreshStartupStatus();
    }

    private void RefreshStartupStatus()
    {
        try
        {
            var status = _startupStatus();
            var wasUpdating = _updating;
            _updating = true;
            try { _startup.IsChecked = status.Enabled; }
            finally { _updating = wasUpdating; }
            _startup.IsEnabled = true;
            _startupDetail.Text = status.Detail;
        }
        catch (Exception ex)
        {
            _startup.IsEnabled = false;
            _startupDetail.Text = "Windows startup status is unavailable: " + ex.Message;
        }
    }

    private void RefreshTheme()
    {
        var next = InstrumentTheme.For(_preferences.Appearance);
        if (next.Light != _theme.Light || _lastHighContrast != SystemParameters.HighContrast || !Resources.Contains(InstrumentTheme.BackgroundKey))
        {
            _theme = next;
            _lastHighContrast = SystemParameters.HighContrast;
            _theme.Apply(this);
        }
    }

    private void UpdateMotionDetail() => _motionDetail.Text = !SystemParameters.ClientAreaAnimation
        ? "Windows has disabled interface animations. thermal kun respects that setting."
        : "Subtle meter transitions. Windows reduced-motion settings take precedence.";

    private static StackPanel Group(string title)
    {
        var panel = new StackPanel { Margin = new Thickness(13, 12, 13, 13) };
        var label = InstrumentTheme.Label(title, 10, InstrumentTheme.SupportKey, true);
        label.Margin = new Thickness(0, 0, 0, 12);
        panel.Children.Add(label);
        return panel;
    }

    private static Border WrapGroup(StackPanel content)
    {
        var border = new Border { Child = content, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 13) };
        border.SetResourceReference(Border.BorderBrushProperty, InstrumentTheme.RuleKey);
        border.SetResourceReference(Border.BackgroundProperty, InstrumentTheme.SurfaceKey);
        return border;
    }

    private static TextBlock BodyText(string text, Thickness? margin = null)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = margin ?? new Thickness(0) };
        block.SetResourceReference(TextBlock.ForegroundProperty, InstrumentTheme.SecondaryKey);
        return block;
    }
}

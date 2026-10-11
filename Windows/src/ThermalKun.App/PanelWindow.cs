using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ThermalKun.Core;

namespace ThermalKun.App;

public sealed class PanelWindow : Window
{
    private Preferences _preferences;
    private InstrumentTheme _theme;
    private Snapshot? _snapshot;
    private TemperatureHistory? _history;
    private readonly DispatcherTimer _statusTimer;
    private TextBlock _temperatureValue = null!, _temperatureUnit = null!, _temperatureSource = null!;
    private TextBlock _peakValue = null!, _live = null!, _timestamp = null!;
    private Ellipse _liveDot = null!;
    private Border _temperatureSection = null!;
    private MetricView _cpu = null!, _gpu = null!, _memory = null!;
    private HistoryGraph _graph = null!;
    private double _resizeStart, _resizeX, _resizeY;
    private bool _restoring;
    private bool _lastHighContrast;

    public event Action? ToggleCompactRequested;
    public event Action? SettingsRequested;
    public event Action? HideRequested;
    public event Action<PanelFrame>? FrameChanged;

    public PanelWindow(Preferences preferences)
    {
        _preferences = preferences.Normalize();
        _theme = InstrumentTheme.For(_preferences.Appearance);
        _lastHighContrast = SystemParameters.HighContrast;
        Title = "thermal kun";
        Width = Height = _preferences.Compact ? 360 : 480;
        MinWidth = MinHeight = 350;
        MaxWidth = MaxHeight = 1600;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;
        ShowInTaskbar = true;
        Topmost = _preferences.AlwaysOnTop;
        WindowStartupLocation = WindowStartupLocation.Manual;
        _theme.Apply(this);
        BuildContent();
        SourceInitialized += (_, _) => DisableRoundedCorners();
        SizeChanged += (_, _) => UpdateGraphHeight();
        KeyDown += OnKeyDown;
        _statusTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _statusTimer.Tick += (_, _) =>
        {
            var next = InstrumentTheme.For(_preferences.Appearance);
            if (next.Light != _theme.Light || _lastHighContrast != SystemParameters.HighContrast)
            {
                _lastHighContrast = SystemParameters.HighContrast;
                _theme = next;
                _theme.Apply(this);
                BuildContent();
                RenderSnapshot();
            }
            UpdateFooter();
        };
        Loaded += (_, _) => _statusTimer.Start();
        Closed += (_, _) => _statusTimer.Stop();
    }

    public void ApplyPreferences(Preferences preferences)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => ApplyPreferences(preferences)); return; }
        _preferences = preferences.Normalize();
        Topmost = _preferences.AlwaysOnTop;
        _theme = InstrumentTheme.For(_preferences.Appearance);
        _theme.Apply(this);
        BuildContent();
        RenderSnapshot();
    }

    public void UpdateSnapshot(Snapshot snapshot, TemperatureHistory history)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => UpdateSnapshot(snapshot, history)); return; }
        _snapshot = snapshot;
        _history = history;
        RenderSnapshot();
    }

    public void CenterOnScreen()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(CenterOnScreen); return; }
        var work = GetWorkArea(null);
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + (work.Height - Height) / 2;
        ReportFrame();
    }

    public void RestoreFrame(PanelFrame? frame)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => RestoreFrame(frame)); return; }
        _restoring = true;
        try
        {
            var size = frame is { } saved && double.IsFinite(saved.Size) ? Math.Clamp(saved.Size, 350, 1600) : _preferences.Compact ? 360 : 480;
            var validPosition = frame is { } position && double.IsFinite(position.X) && double.IsFinite(position.Y);
            var work = GetWorkArea(validPosition ? frame : null);
            size = Math.Min(size, Math.Max(350, Math.Min(work.Width, work.Height)));
            Width = Height = size;
            Left = validPosition ? Math.Clamp(frame!.X, work.Left, Math.Max(work.Left, work.Right - size)) : work.Left + (work.Width - size) / 2;
            Top = validPosition ? Math.Clamp(frame!.Y, work.Top, Math.Max(work.Top, work.Bottom - size)) : work.Top + (work.Height - size) / 2;
        }
        finally { _restoring = false; }
    }

    private void BuildContent()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(BuildHeader());
        var sections = new StackPanel { Margin = new Thickness(15, 10, 15, 10) };
        sections.Children.Add(BuildTemperature());
        sections.Children.Add(BuildLoad());
        var history = new StackPanel { Margin = new Thickness(0, 8, 0, 0), Visibility = _preferences.Compact ? Visibility.Collapsed : Visibility.Visible };
        history.Children.Add(SectionLabel("02", "CPU TEMP HISTORY", InstrumentTheme.SupportKey));
        _graph = new HistoryGraph { Margin = new Thickness(0, 7, 0, 0) };
        history.Children.Add(_graph);
        sections.Children.Add(history);
        sections.Children.Add(BuildPeak());
        var scroll = new ScrollViewer
        {
            Content = sections, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, PanningMode = PanningMode.VerticalOnly
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        var footer = BuildFooter();
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        var outline = new Border { BorderThickness = new Thickness(1), Child = root };
        outline.SetResourceReference(Border.BorderBrushProperty, InstrumentTheme.RuleKey);
        var overlay = new Grid();
        overlay.Children.Add(outline);
        var strip = new Border { Width = 82, Height = 3, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(25, 0, 0, 0), IsHitTestVisible = false };
        strip.SetResourceReference(Border.BackgroundProperty, InstrumentTheme.SupportKey);
        overlay.Children.Add(strip);
        overlay.Children.Add(BuildResizeHandle());
        Content = overlay;
        UpdateGraphHeight();
    }

    private Border BuildHeader()
    {
        var header = new Grid { Margin = new Thickness(15, 10, 15, 9) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 3; i++) header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var dragContent = new Grid();
        dragContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(31) });
        dragContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var thermometer = Glyph("M 10,2 Q 8,2 8,4 L 8,17 C 3,20 5,26 10,26 C 15,26 17,20 12,17 L 12,4 Q 12,2 10,2 M 10,9 L 10,22", 22, 26, InstrumentTheme.AccentKey);
        thermometer.VerticalAlignment = VerticalAlignment.Center;
        dragContent.Children.Add(thermometer);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(InstrumentTheme.Label("thermal kun", 15, InstrumentTheme.SupportKey, true));
        names.Children.Add(InstrumentTheme.Label("SYSTEM TELEMETRY", 8, InstrumentTheme.SecondaryKey, true));
        Grid.SetColumn(names, 1);
        dragContent.Children.Add(names);
        var drag = new Border { Background = Brushes.Transparent, Child = dragContent, Height = 32, Cursor = Cursors.Hand, ToolTip = "Drag here to move the panel" };
        drag.MouseLeftButtonDown += (_, args) =>
        {
            if (args.ClickCount != 1) return;
            args.Handled = true;
            try { DragMove(); }
            catch (InvalidOperationException) { return; }
            ReportFrame();
        };
        header.Children.Add(drag);
        var compactPath = _preferences.Compact
            ? "M 6,6 L 1,1 M 1,5 L 1,1 L 5,1 M 8,8 L 13,13 M 9,13 L 13,13 L 13,9"
            : "M 1,1 L 6,6 M 2,6 L 6,6 L 6,2 M 13,13 L 8,8 M 8,12 L 8,8 L 12,8";
        var compact = HeaderButton(compactPath, _preferences.Compact ? "Expand details (Ctrl+C)" : "Compact view (Ctrl+C)", () => ToggleCompactRequested?.Invoke());
        var settings = HeaderButton("M 1,3 L 13,3 M 1,7 L 13,7 M 1,11 L 13,11 M 5,1 L 5,5 M 10,5 L 10,9 M 4,9 L 4,13", "Settings (Ctrl+,)", () => SettingsRequested?.Invoke());
        var hide = HeaderButton("M 2,7 L 12,7", "Hide panel (Escape)", () => HideRequested?.Invoke());
        foreach (var (button, column) in new[] { (compact, 1), (settings, 2), (hide, 3) })
        {
            Grid.SetColumn(button, column);
            header.Children.Add(button);
        }
        var border = new Border { Child = header, BorderThickness = new Thickness(0, 0, 0, 1) };
        border.SetResourceReference(Border.BackgroundProperty, InstrumentTheme.HeaderKey);
        border.SetResourceReference(Border.BorderBrushProperty, InstrumentTheme.RuleKey);
        return border;
    }

    private Border BuildTemperature()
    {
        var rows = new Grid();
        rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rows.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2) });
        var content = new Grid { MinHeight = 64, Margin = new Thickness(0, 0, 0, 7) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(SectionLabel("01", "CPU TEMP", InstrumentTheme.AccentKey));
        _temperatureSource = InstrumentTheme.Label("", 8);
        _temperatureSource.Margin = new Thickness(0, 7, 8, 0);
        labels.Children.Add(_temperatureSource);
        content.Children.Add(labels);
        var reading = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _temperatureValue = InstrumentTheme.Label("Unavailable", 15, InstrumentTheme.SecondaryKey);
        _temperatureValue.FontWeight = FontWeights.Light;
        _temperatureUnit = InstrumentTheme.Label("°C", 15);
        _temperatureUnit.Margin = new Thickness(4, 0, 0, 9);
        _temperatureUnit.VerticalAlignment = VerticalAlignment.Bottom;
        _temperatureUnit.Visibility = Visibility.Collapsed;
        reading.Children.Add(_temperatureValue);
        reading.Children.Add(_temperatureUnit);
        Grid.SetColumn(reading, 1);
        content.Children.Add(reading);
        rows.Children.Add(content);
        var line = new Border();
        line.SetResourceReference(Border.BackgroundProperty, InstrumentTheme.AccentKey);
        Grid.SetRow(line, 1);
        rows.Children.Add(line);
        _temperatureSection = new Border { Child = rows };
        AutomationProperties.SetName(_temperatureSection, "CPU temperature");
        return _temperatureSection;
    }

    private Grid BuildLoad()
    {
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        var columns = _preferences.Compact ? 2 : 3;
        for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _cpu = new MetricView("CPU LOAD", InstrumentTheme.AccentKey, AnimateMeters) { Margin = new Thickness(0, 0, 11, 0) };
        _gpu = new MetricView("GPU LOAD", InstrumentTheme.SupportKey, AnimateMeters) { Margin = new Thickness(0, 0, 11, 0) };
        _memory = new MetricView("MEMORY", InstrumentTheme.SupportKey, AnimateMeters);
        grid.Children.Add(_cpu);
        if (!_preferences.Compact)
        {
            Grid.SetColumn(_gpu, 1);
            grid.Children.Add(_gpu);
        }
        Grid.SetColumn(_memory, columns - 1);
        grid.Children.Add(_memory);
        return grid;
    }

    private Grid BuildPeak()
    {
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0), Visibility = _preferences.Compact ? Visibility.Collapsed : Visibility.Visible };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = InstrumentTheme.Label("CPU PEAK", 9, InstrumentTheme.SecondaryKey, true);
        label.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(label);
        _peakValue = InstrumentTheme.Label("Unavailable", 9, InstrumentTheme.AccentKey);
        _peakValue.Margin = new Thickness(10, 0, 0, 0);
        _peakValue.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_peakValue, 1);
        grid.Children.Add(_peakValue);
        var reset = new Button
        {
            Content = InstrumentTheme.Label("↶ RESET", 9, InstrumentTheme.SecondaryKey, true),
            Padding = new Thickness(5, 2, 5, 2), ToolTip = "Reset the CPU temperature peak only. Recorded history is kept."
        };
        AutomationProperties.SetName(reset, "Reset peak CPU temperature");
        reset.Click += (_, _) => { _history?.ResetPeak(); RenderSnapshot(); };
        Grid.SetColumn(reset, 2);
        grid.Children.Add(reset);
        return grid;
    }

    private Grid BuildFooter()
    {
        var footer = new Grid { Margin = new Thickness(15, 5, 20, 12) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _liveDot = new Ellipse { Width = 4, Height = 4, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
        _liveDot.SetResourceReference(Shape.FillProperty, InstrumentTheme.SecondaryKey);
        footer.Children.Add(_liveDot);
        _live = InstrumentTheme.Label("WAITING FOR DATA", 8);
        Grid.SetColumn(_live, 1);
        footer.Children.Add(_live);
        _timestamp = InstrumentTheme.Label("", 8);
        Grid.SetColumn(_timestamp, 2);
        footer.Children.Add(_timestamp);
        return footer;
    }

    private Thumb BuildResizeHandle()
    {
        var thumb = new Thumb
        {
            Width = 20, Height = 20, Margin = new Thickness(0, 0, 3, 3),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = Cursors.SizeNWSE, Focusable = true, ToolTip = "Resize panel; use arrow keys while focused"
        };
        AutomationProperties.SetName(thumb, "Resize square panel");
        var canvas = new FrameworkElementFactory(typeof(Grid));
        canvas.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
        var mark = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        mark.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 5,16 L 16,5 M 10,16 L 16,10"));
        mark.SetValue(Shape.StrokeProperty, _theme.Support);
        mark.SetValue(Shape.StrokeThicknessProperty, 1.0);
        canvas.AppendChild(mark);
        thumb.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = canvas };
        thumb.DragStarted += (_, _) => { _resizeStart = Width; _resizeX = _resizeY = 0; };
        thumb.DragDelta += (_, args) =>
        {
            _resizeX += args.HorizontalChange;
            _resizeY += args.VerticalChange;
            var delta = Math.Abs(_resizeX) >= Math.Abs(_resizeY) ? _resizeX : _resizeY;
            Width = Height = Math.Clamp(_resizeStart + delta, 350, 1600);
        };
        thumb.DragCompleted += (_, _) => ReportFrame();
        thumb.KeyDown += (_, args) =>
        {
            if (args.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;
            var delta = args.Key is Key.Left or Key.Up ? -10 : 10;
            Width = Height = Math.Clamp(Width + delta, 350, 1600);
            ReportFrame();
            args.Handled = true;
        };
        return thumb;
    }

    private void RenderSnapshot()
    {
        if (_snapshot is not { } sample) { UpdateFooter(); return; }
        var temperature = sample.CpuTemperature;
        _temperatureValue.Text = ReadingText(temperature, "0.0");
        _temperatureValue.FontSize = temperature.IsAvailable ? 48 : 15;
        _temperatureValue.SetResourceReference(TextBlock.ForegroundProperty, temperature.IsAvailable ? InstrumentTheme.AccentKey : InstrumentTheme.SecondaryKey);
        _temperatureUnit.Visibility = temperature.IsAvailable ? Visibility.Visible : Visibility.Collapsed;
        _temperatureSource.Text = temperature.Source.ToUpperInvariant();
        _temperatureSection.ToolTip = ReadingHelp(temperature);
        AutomationProperties.SetName(_temperatureSection, "CPU temperature " + _temperatureValue.Text + (temperature.IsAvailable ? " degrees Celsius" : ""));
        _cpu.Update(ReadingText(sample.CpuLoad, "0.0", "%"), sample.CpuLoad.IsAvailable ? sample.CpuLoad.Value / 100 : null, ReadingHelp(sample.CpuLoad), sample.CpuLoad.IsAvailable);
        _gpu.Update(ReadingText(sample.GpuLoad, "0.0", "%"), sample.GpuLoad.IsAvailable ? sample.GpuLoad.Value / 100 : null, ReadingHelp(sample.GpuLoad), sample.GpuLoad.IsAvailable);
        var memoryFraction = sample.MemoryUsedGiB.IsAvailable && sample.MemoryTotalGiB.IsAvailable && sample.MemoryTotalGiB.Value > 0
            ? sample.MemoryUsedGiB.Value / sample.MemoryTotalGiB.Value : null;
        _memory.Update(ReadingText(sample.MemoryUsedGiB, "0.0", " GiB"), memoryFraction,
            ReadingHelp(sample.MemoryUsedGiB) + "\n" + ReadingHelp(sample.MemoryTotalGiB), sample.MemoryUsedGiB.IsAvailable);
        _peakValue.Text = _history?.Peak is double peak && double.IsFinite(peak) ? peak.ToString("0.0°C", CultureInfo.InvariantCulture) : "Unavailable";
        _graph.Update(_history?.Points ?? [], _theme, _preferences.UpdateInterval);
        UpdateFooter();
    }

    private void UpdateFooter()
    {
        var stale = _snapshot is { } snapshot && snapshot.IsStale(DateTimeOffset.UtcNow, _preferences.UpdateInterval);
        _live.Text = stale ? "STALE · UPDATES PAUSED" : _snapshot is null ? "WAITING FOR DATA" : $"LIVE · {_preferences.UpdateInterval}s";
        _live.SetResourceReference(TextBlock.ForegroundProperty, stale ? InstrumentTheme.AmberKey : InstrumentTheme.SecondaryKey);
        _liveDot.SetResourceReference(Shape.FillProperty, stale ? InstrumentTheme.AmberKey : _snapshot is null ? InstrumentTheme.SecondaryKey : InstrumentTheme.AccentKey);
        _timestamp.Text = _snapshot?.SampledAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? "";
        _live.ToolTip = stale
            ? "Displayed readings are from the last sample. Sampling has paused or is delayed."
            : "LIVE indicates a completed app sampling cycle, including unavailable sensors. The timestamp is when this app sampled; an external provider's sensor age cannot be verified.";
        _timestamp.ToolTip = _snapshot?.SampledAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
    }

    private void UpdateGraphHeight()
    {
        if (_graph is not null) _graph.Height = Math.Clamp(Height - 295, 48, 260);
    }

    private bool AnimateMeters() => _preferences.Animations && SystemParameters.ClientAreaAnimation;

    private void ReportFrame()
    {
        if (!_restoring && double.IsFinite(Left) && double.IsFinite(Top)) FrameChanged?.Invoke(new(Left, Top, Width));
    }

    private void OnKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape) { HideRequested?.Invoke(); args.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && args.Key == Key.C) { ToggleCompactRequested?.Invoke(); args.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && args.Key == Key.OemComma) { SettingsRequested?.Invoke(); args.Handled = true; }
    }

    private static StackPanel SectionLabel(string number, string title, string colorKey)
    {
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        var numberText = InstrumentTheme.Label(number, 9, colorKey, true);
        numberText.Margin = new Thickness(0, 0, 5, 0);
        label.Children.Add(numberText);
        label.Children.Add(InstrumentTheme.Label(title, 9, InstrumentTheme.SecondaryKey, true));
        return label;
    }

    private static System.Windows.Shapes.Path Glyph(string path, double width, double height, string colorKey)
    {
        var glyph = new System.Windows.Shapes.Path { Data = Geometry.Parse(path), Width = width, Height = height, StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        glyph.SetResourceReference(Shape.StrokeProperty, colorKey);
        return glyph;
    }

    private static Button HeaderButton(string path, string label, Action action)
    {
        var button = new Button { Content = Glyph(path, 14, 14, InstrumentTheme.SecondaryKey), Width = 27, Height = 24, Margin = new Thickness(5, 0, 0, 0), Padding = new Thickness(3), ToolTip = label, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action();
        return button;
    }

    private static string ReadingText(Reading reading, string format, string suffix = "") => reading.IsAvailable
        ? reading.Value!.Value.ToString(format, CultureInfo.InvariantCulture) + suffix : reading.MissingLabel;

    private static string ReadingHelp(Reading reading) => $"Source: {reading.Source}\nState: {reading.State}\n{reading.Detail}";

    private sealed class MetricView : StackPanel
    {
        private readonly TextBlock _value;
        private readonly Grid _bar;
        private readonly Border _fill;
        private readonly Func<bool> _animate;
        private double? _fraction;
        public Grid LabelGrid { get; }

        public MetricView(string label, string colorKey, Func<bool> animate)
        {
            _animate = animate;
            LabelGrid = new Grid();
            LabelGrid.Children.Add(InstrumentTheme.Label(label, 9, InstrumentTheme.SecondaryKey, true));
            Children.Add(LabelGrid);
            _value = InstrumentTheme.Label("Unavailable", 12, colorKey);
            _value.Margin = new Thickness(0, 5, 0, 5);
            Children.Add(_value);
            _bar = new Grid { Height = 2 };
            _bar.SetResourceReference(Panel.BackgroundProperty, InstrumentTheme.RuleKey);
            _fill = new Border { HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            _fill.SetResourceReference(Border.BackgroundProperty, colorKey);
            _bar.Children.Add(_fill);
            Children.Add(_bar);
            _bar.SizeChanged += (_, _) => SetBar(false);
            AutomationProperties.SetName(this, label);
        }

        public void Update(string value, double? fraction, string help, bool available)
        {
            _value.Text = value;
            _value.FontSize = available ? 17 : 12;
            _fraction = fraction is double f && double.IsFinite(f) ? Math.Clamp(f, 0, 1) : null;
            ToolTip = help;
            SetBar(_animate());
            AutomationProperties.SetName(this, ((TextBlock)LabelGrid.Children[0]).Text + " " + value);
        }

        private void SetBar(bool animate)
        {
            var target = _bar.ActualWidth * (_fraction ?? 0);
            var previous = _fill.ActualWidth;
            _fill.BeginAnimation(WidthProperty, null);
            _fill.Width = target;
            if (animate && _fraction is not null && Math.Abs(previous - target) > 0.1)
                _fill.BeginAnimation(WidthProperty, new DoubleAnimation(previous, target, TimeSpan.FromSeconds(0.2)) { FillBehavior = FillBehavior.Stop });
        }
    }

    private Rect GetWorkArea(PanelFrame? proposed)
    {
        var source = PresentationSource.FromVisual(this) as HwndSource;
        var toDevice = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var handle = new WindowInteropHelper(this).Handle;
        IntPtr monitor;
        if (proposed is { } frame)
        {
            var origin = toDevice.Transform(new Point(frame.X, frame.Y));
            var end = toDevice.Transform(new Point(frame.X + frame.Size, frame.Y + frame.Size));
            var rectangle = new NativeRect { Left = (int)origin.X, Top = (int)origin.Y, Right = (int)end.X, Bottom = (int)end.Y };
            monitor = MonitorFromRect(ref rectangle, 2);
        }
        else monitor = MonitorFromWindow(handle, 2);
        var info = new MonitorInformation { Size = Marshal.SizeOf<MonitorInformation>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return SystemParameters.WorkArea;
        var topLeft = fromDevice.Transform(new Point(info.Work.Left, info.Work.Top));
        var bottomRight = fromDevice.Transform(new Point(info.Work.Right, info.Work.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private void DisableRoundedCorners()
    {
        try
        {
            var preference = 1; // DWMWCP_DONOTROUND; ignored on Windows versions without this attribute.
            _ = DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref preference, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInformation { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect rectangle, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInformation info);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}

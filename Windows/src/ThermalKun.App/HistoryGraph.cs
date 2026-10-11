using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using ThermalKun.Core;

namespace ThermalKun.App;

public sealed class HistoryGraph : FrameworkElement
{
    private HistoryPoint[] _points = [];
    private InstrumentTheme _theme = InstrumentTheme.For(Appearance.Dark);
    private int? _hoveredIndex;
    private int _interval = 2;
    private Rect Plot => new(29, 2, Math.Max(1, ActualWidth - 30), Math.Max(1, ActualHeight - 20));

    public HistoryGraph()
    {
        MinHeight = 48;
        Focusable = true;
        ToolTip = "Hover or use Left and Right arrows to inspect recorded temperatures. Missing samples leave gaps. The scale follows measured values in 10°C steps.";
        AutomationProperties.SetName(this, "Recent CPU temperature history");
        MouseMove += OnMouseMove;
        MouseLeave += (_, _) => { _hoveredIndex = null; InvalidateVisual(); };
        KeyDown += OnKeyDown;
    }

    internal void Update(IReadOnlyList<HistoryPoint> points, InstrumentTheme theme, int interval)
    {
        _points = points.TakeLast(TemperatureHistory.Capacity).ToArray();
        _theme = theme;
        _interval = interval;
        if (_hoveredIndex >= _points.Length) _hoveredIndex = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        drawing.DrawRectangle(_theme.Background, null, new Rect(RenderSize));
        var plot = Plot;
        var values = _points.Where(p => p.Temperature is double v && double.IsFinite(v)).Select(p => p.Temperature!.Value).ToArray();
        var lower = Math.Floor((values.Length > 0 ? values.Min() : 20) / 10) * 10;
        var upper = Math.Max(lower + 10, Math.Ceiling((values.Length > 0 ? values.Max() : 30) / 10) * 10);
        var gridPen = new Pen(_theme.Rule, 0.5) { DashStyle = new DashStyle([2, 3], 0) };
        for (var row = 0; row <= 2; row++)
        {
            var y = plot.Top + plot.Height * row / 2;
            drawing.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }
        for (var column = 0; column <= 5; column++)
        {
            var x = plot.Left + plot.Width * column / 5;
            drawing.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }
        if (values.Length == 0)
        {
            var missing = Format("Unavailable", 11, _theme.Secondary);
            drawing.DrawText(missing, new Point(plot.Left + (plot.Width - missing.Width) / 2,
                plot.Top + (plot.Height - missing.Height) / 2));
        }
        else
        {
            DrawText(drawing, upper.ToString("0°", CultureInfo.InvariantCulture), 8, _theme.Secondary, 0, plot.Top - 2);
            DrawText(drawing, lower.ToString("0°", CultureInfo.InvariantCulture), 8, _theme.Secondary, 0, plot.Bottom - 9);
            var geometry = new StreamGeometry();
            Point? lastPoint = null;
            using (var context = geometry.Open())
            {
                var connected = false;
                DateTimeOffset? previousTimestamp = null;
                foreach (var sample in _points)
                {
                    if (sample.Temperature is not double value || !double.IsFinite(value))
                    {
                        connected = false;
                        previousTimestamp = sample.Timestamp;
                        continue;
                    }
                    // A missed or suspended sampling period is a gap even if no null point was inserted.
                    if (previousTimestamp is { } previous && sample.Timestamp - previous > TimeSpan.FromSeconds(Math.Max(6, _interval * 3)))
                        connected = false;
                    var point = new Point(X(sample.Timestamp), plot.Top + plot.Height * (1 - (value - lower) / (upper - lower)));
                    if (connected) context.LineTo(point, true, false);
                    else context.BeginFigure(point, false, false);
                    connected = true;
                    previousTimestamp = sample.Timestamp;
                    lastPoint = point;
                }
            }
            var glow = new Pen(_theme.Accent, 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            drawing.PushOpacity(0.13);
            drawing.DrawGeometry(null, glow, geometry);
            drawing.Pop();
            drawing.DrawGeometry(null, new Pen(_theme.Accent, 1.2), geometry);
            if (lastPoint is { } last) drawing.DrawEllipse(_theme.Accent, null, last, 1.7, 1.7);
        }
        if (_hoveredIndex is { } index && index < _points.Length)
        {
            var sample = _points[index];
            var x = X(sample.Timestamp);
            drawing.DrawLine(new Pen(_theme.Secondary, 1) { DashStyle = new DashStyle([2, 3], 0) },
                new Point(x, plot.Top), new Point(x, plot.Bottom));
            var time = sample.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            var value = sample.Temperature is double v && double.IsFinite(v) ? v.ToString("0.0°C", CultureInfo.InvariantCulture) : "Unavailable";
            DrawText(drawing, time, 8, _theme.Secondary, plot.Left, plot.Bottom + 4);
            DrawText(drawing, "CPU " + value, 8, _theme.Accent, plot.Left + 57, plot.Bottom + 4);
        }
        else
        {
            var duration = _points.Length == 0 ? "NO SAMPLES" : Duration(_points[^1].Timestamp - _points[0].Timestamp);
            DrawText(drawing, duration, 8, _theme.Secondary, plot.Left, plot.Bottom + 4);
            var now = Format("NOW", 8, _theme.Secondary);
            drawing.DrawText(now, new Point(plot.Right - now.Width, plot.Bottom + 4));
        }
    }

    private double X(DateTimeOffset timestamp)
    {
        var plot = Plot;
        if (_points.Length < 2 || _points[^1].Timestamp <= _points[0].Timestamp) return plot.Right;
        return plot.Left + plot.Width * (timestamp - _points[0].Timestamp).TotalSeconds /
            (_points[^1].Timestamp - _points[0].Timestamp).TotalSeconds;
    }

    private void OnMouseMove(object sender, MouseEventArgs args)
    {
        if (_points.Length == 0) return;
        var x = args.GetPosition(this).X;
        _hoveredIndex = Enumerable.Range(0, _points.Length).MinBy(i => Math.Abs(X(_points[i].Timestamp) - x));
        InvalidateVisual();
    }

    private void OnKeyDown(object sender, KeyEventArgs args)
    {
        if (_points.Length == 0 || args.Key is not (Key.Left or Key.Right or Key.Home or Key.End)) return;
        var selected = _hoveredIndex ?? (_points.Length - 1);
        _hoveredIndex = args.Key switch
        {
            Key.Home => 0, Key.End => _points.Length - 1,
            Key.Left => Math.Max(0, selected - 1), _ => Math.Min(_points.Length - 1, selected + 1)
        };
        args.Handled = true;
        InvalidateVisual();
    }

    private FormattedText Format(string text, double size, Brush brush) => new(text, CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight, new Typeface("Consolas"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private void DrawText(DrawingContext drawing, string text, double size, Brush brush, double x, double y) =>
        drawing.DrawText(Format(text, size, brush), new Point(x, y));

    private static string Duration(TimeSpan span) => span.TotalSeconds >= 60
        ? "LAST " + Math.Max(0, span.TotalMinutes).ToString("0.0", CultureInfo.InvariantCulture) + " MIN"
        : "LAST " + Math.Max(0, span.TotalSeconds).ToString("0", CultureInfo.InvariantCulture) + " SEC";
}

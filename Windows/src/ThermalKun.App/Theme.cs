using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using ThermalKun.Core;

namespace ThermalKun.App;

internal sealed class InstrumentTheme
{
    internal const string BackgroundKey = "ThermalBackground";
    internal const string HeaderKey = "ThermalHeader";
    internal const string SurfaceKey = "ThermalSurface";
    internal const string HoverKey = "ThermalHover";
    internal const string TextKey = "ThermalText";
    internal const string SecondaryKey = "ThermalSecondary";
    internal const string RuleKey = "ThermalRule";
    internal const string AccentKey = "ThermalAccent";
    internal const string SupportKey = "ThermalSupport";
    internal const string AmberKey = "ThermalAmber";

    public bool Light { get; }
    public Brush Background { get; }
    public Brush Header { get; }
    public Brush Surface { get; }
    public Brush Hover { get; }
    public Brush Text { get; }
    public Brush Secondary { get; }
    public Brush Rule { get; }
    public Brush Accent { get; }
    public Brush Support { get; }
    public Brush Amber { get; }

    private InstrumentTheme(bool light)
    {
        Light = light;
        if (SystemParameters.HighContrast)
        {
            Background = Header = SystemColors.WindowBrush;
            Surface = Hover = SystemColors.ControlBrush;
            Text = Secondary = SystemColors.WindowTextBrush;
            Rule = SystemColors.WindowTextBrush;
            Accent = Support = Amber = SystemColors.HighlightBrush;
            return;
        }
        Background = ColorBrush(light ? "#F0F0F5" : "#0E0F14");
        Header = ColorBrush(light ? "#E6E0F2" : "#181323");
        Surface = ColorBrush(light ? "#E0E0EB" : "#1A1B24");
        Hover = ColorBrush(light ? "#D4C9E8" : "#301F45");
        Text = ColorBrush(light ? "#1A1726" : "#E6EDEA");
        Secondary = ColorBrush(light ? "#525466" : "#919EA8");
        Rule = ColorBrush(light ? "#B3B3C7" : "#3B414D");
        Accent = ColorBrush(light ? "#4D6B04" : "#BAFA38");
        Support = ColorBrush(light ? "#782EC4" : "#B866FF");
        Amber = ColorBrush(light ? "#915703" : "#FFB333");
    }

    public static InstrumentTheme For(Appearance appearance) => new(appearance == Appearance.Light ||
        (appearance == Appearance.System && SystemUsesLightTheme()));

    private static bool SystemUsesLightTheme()
    {
        try
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1) is not int value || value != 0;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return true;
        }
    }

    public void Apply(Window window)
    {
        var resources = window.Resources;
        resources[BackgroundKey] = Background;
        resources[HeaderKey] = Header;
        resources[SurfaceKey] = Surface;
        resources[HoverKey] = Hover;
        resources[TextKey] = Text;
        resources[SecondaryKey] = Secondary;
        resources[RuleKey] = Rule;
        resources[AccentKey] = Accent;
        resources[SupportKey] = Support;
        resources[AmberKey] = Amber;
        window.Background = Background;
        window.Foreground = Text;
        if (resources.Contains(typeof(Button))) return;

        var textStyle = new Style(typeof(TextBlock));
        textStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension(TextKey)));
        resources[typeof(TextBlock)] = textStyle;
        var buttonStyle = new Style(typeof(Button));
        buttonStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(SecondaryKey)));
        buttonStyle.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(SurfaceKey)));
        buttonStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension(RuleKey)));
        buttonStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        buttonStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 4, 8, 4)));
        buttonStyle.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        buttonStyle.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Button)) { VisualTree = border }));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(HoverKey)));
        hover.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(AccentKey)));
        buttonStyle.Triggers.Add(hover);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension(AccentKey)));
        buttonStyle.Triggers.Add(focus);
        resources[typeof(Button)] = buttonStyle;
        foreach (var type in new[] { typeof(CheckBox), typeof(RadioButton) })
        {
            var style = new Style(type);
            style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(TextKey)));
            style.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily("Segoe UI")));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
            resources[type] = style;
        }
    }

    internal static TextBlock Label(string text, double size = 9, string colorKey = SecondaryKey, bool bold = false)
    {
        var label = new TextBlock
        {
            Text = text, FontFamily = new FontFamily("Consolas"), FontSize = size,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, colorKey);
        return label;
    }

    private static Brush ColorBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

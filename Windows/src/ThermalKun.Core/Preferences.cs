using System.Text.Json;

namespace ThermalKun.Core;
public enum Appearance { Dark, Light, System }
public sealed record PanelFrame(double X, double Y, double Size);
public sealed record Preferences
{
    public Appearance Appearance { get; init; } = Appearance.Dark;
    public bool Compact { get; init; }
    public bool AlwaysOnTop { get; init; }
    public bool Animations { get; init; } = true;
    public int UpdateInterval { get; init; } = 2;
    public bool LibreHardwareMonitorEnabled { get; init; }
    public bool BundledTemperatureEnabled { get; init; }
    public PanelFrame? DetailFrame { get; init; }
    public PanelFrame? CompactFrame { get; init; }
    public Preferences Normalize() => this with
    {
        Appearance = Enum.IsDefined(Appearance) ? Appearance : Appearance.Dark,
        UpdateInterval = new[] { 1, 2, 5, 10 }.Contains(UpdateInterval) ? UpdateInterval : 2,
        DetailFrame = NormalizeFrame(DetailFrame), CompactFrame = NormalizeFrame(CompactFrame)
    };
    private static PanelFrame? NormalizeFrame(PanelFrame? frame) => frame is { } f &&
        double.IsFinite(f.X) && double.IsFinite(f.Y) && double.IsFinite(f.Size) ? f with { Size = Math.Clamp(f.Size, 350, 1600) } : null;
}
public sealed class PreferencesStore(string path)
{
    public string Path => path;
    public Preferences Load()
    {
        try { return (JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) ?? new()).Normalize(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save(Preferences preferences)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(preferences.Normalize(), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
}

using Microsoft.Win32;

namespace ThermalKun.App;
// Registration is only called by an explicit Settings action. Never alter StartupApproved.
public sealed class StartupService
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string Name = "ThermalKunWindows";
    private string Command => $"\"{Environment.ProcessPath}\" --startup";
    public (bool Enabled, string Detail) Status()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunPath, false);
            var registered = run?.GetValue(Name) as string;
            if (registered is null) return (false, "Off. No startup entry is registered. Enable only after extracting to a stable location.");
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedPath, false);
            var approval = approved?.GetValue(Name) as byte[];
            if (approval is { Length: > 0 } && approval[0] != 2 && approval[0] != 6)
                return (false, "Registered but disabled by Windows. Enable thermal kun in Windows Settings > Apps > Startup, or turn off this registration here.");
            if (!string.Equals(registered, Command, StringComparison.OrdinalIgnoreCase))
                return (false, "A startup entry refers to another location. Turn it off here, then enable from the final installation folder.");
            return (true, "On for your Windows account. Registration points to this executable. Actual logout/login remains unverified.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        { return (false, $"Unable to read startup registration: {ex.Message}"); }
    }
    public void Set(bool enabled)
    {
        if (enabled && (Environment.ProcessPath is null || !string.Equals(System.IO.Path.GetFileName(Environment.ProcessPath), "ThermalKun.exe", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Run the published ThermalKun.exe from a stable folder before enabling startup.");
        using var run = Registry.CurrentUser.CreateSubKey(RunPath, true);
        if (enabled) run.SetValue(Name, Command, RegistryValueKind.String);
        else run.DeleteValue(Name, false);
        if (enabled && !Status().Enabled) throw new InvalidOperationException(Status().Detail);
    }
}

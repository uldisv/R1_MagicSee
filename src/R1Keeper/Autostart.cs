using Microsoft.Win32;
using R1Keeper.Core;

namespace R1Keeper;

/// <summary>
/// "Start with Windows" for the current user, through the per-user Run key (no admin
/// rights needed). The registry is the only place this setting lives.
/// </summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "R1Keeper";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (!enabled) key.DeleteValue(ValueName, throwOnMissingValue: false);
        else if (Environment.ProcessPath is { } path) key.SetValue(ValueName, AutostartPath.Command(path));
    }

    /// <summary>If autostart is on but its file is gone (the program was moved), point it at this copy.</summary>
    public static void RepairPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null) return;
        var repaired = AutostartPath.Repaired(key.GetValue(ValueName) as string, Environment.ProcessPath, File.Exists);
        if (repaired is not null) key.SetValue(ValueName, repaired);
    }
}

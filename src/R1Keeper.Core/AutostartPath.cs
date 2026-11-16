namespace R1Keeper.Core;

/// <summary>
/// Decides whether the "start with Windows" registry entry needs to be pointed at the
/// running copy. It is only repaired when the registered file is gone (the program was
/// moved); a second copy that merely happens to be run — a debug build, say — must not
/// take autostart over.
/// </summary>
public static class AutostartPath
{
    private const string ExeName = "R1Keeper.exe";

    public static string Command(string processPath) => "\"" + processPath + "\"";

    /// <returns>The new registry value, or null when nothing should change.</returns>
    public static string? Repaired(string? registered, string? processPath, Func<string, bool> fileExists)
    {
        if (registered is null) return null; // autostart is off
        if (processPath is null || !string.Equals(Path.GetFileName(processPath), ExeName, StringComparison.OrdinalIgnoreCase)) return null;
        string registeredPath = registered.Trim().Trim('"');
        if (string.Equals(registeredPath, processPath, StringComparison.OrdinalIgnoreCase)) return null;
        if (fileExists(registeredPath)) return null;
        return Command(processPath);
    }
}

using System.Globalization;
using System.Text.Json;

namespace R1Keeper.Core;

/// <summary>Persisted user settings. The pult address is the only device the program may act on.</summary>
public sealed class Settings
{
    public const string DefaultNamePrefix = "Magicsee R1";

    /// <summary>12 upper-case hex digits, e.g. "FFFF385D4001"; null = no pult learned yet.</summary>
    public string? PultAddress { get; set; }

    public string NamePrefix { get; set; } = DefaultNamePrefix;

    public bool Paused { get; set; }

    public static string FormatAddress(ulong address) => address.ToString("X12", CultureInfo.InvariantCulture);

    public static ulong? ParseAddress(string? text)
    {
        if (text is null || text.Length != 12) return null;
        return ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    public static string DisplayAddress(ulong address)
    {
        var hex = FormatAddress(address);
        return string.Join(':', Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
    }
}

public interface ISettingsStore
{
    Settings Load();
    void Save(Settings settings);
}

/// <summary>JSON file store. Load never throws: a missing or unreadable file yields defaults.</summary>
public sealed class JsonSettingsStore(string filePath) : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public Settings Load()
    {
        try
        {
            if (!File.Exists(filePath)) return new Settings();
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(filePath), Options) ?? new Settings();
            if (string.IsNullOrWhiteSpace(settings.NamePrefix)) settings.NamePrefix = Settings.DefaultNamePrefix;
            return settings;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new Settings();
        }
    }

    public void Save(Settings settings)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        // Write to a temp file and rename, so an interrupted write cannot corrupt the settings.
        var temp = filePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, filePath, overwrite: true);
    }
}

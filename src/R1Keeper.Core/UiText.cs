using System.Globalization;

namespace R1Keeper.Core;

public enum UiLanguage
{
    English,
    Latvian,
}

public static class UiLanguages
{
    /// <summary>Used when no language has been chosen, or the stored choice is not recognised.</summary>
    public const UiLanguage Default = UiLanguage.English;

    /// <summary>In menu order.</summary>
    public static IReadOnlyList<UiLanguage> All { get; } = [UiLanguage.English, UiLanguage.Latvian];

    /// <summary>The stored form. The installer writes these same codes.</summary>
    public static string Code(UiLanguage language) => language switch
    {
        UiLanguage.Latvian => "lv",
        _ => "en",
    };

    public static UiLanguage Parse(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "lv" => UiLanguage.Latvian,
        "en" => UiLanguage.English,
        _ => Default,
    };

    /// <summary>Each language's name in that language, so it can be found whatever the menu shows.</summary>
    public static string NativeName(UiLanguage language) => language switch
    {
        UiLanguage.Latvian => "Latviešu",
        _ => "English",
    };
}

/// <summary>
/// Every text the user can see, in one language. The properties are required, so a
/// language that misses a text does not compile.
/// </summary>
public sealed class UiText
{
    private const string Title = "R1 Keeper";

    public required string StateAbsent { get; init; }
    public required string StatePairing { get; init; }
    public required string StateConnected { get; init; }
    public required string StatePaused { get; init; }
    public required string MusicMode { get; init; }
    /// <summary>{0} is the pult's address.</summary>
    public required string PultLearnedFormat { get; init; }
    public required string Pause { get; init; }
    public required string Resume { get; init; }
    public required string Forget { get; init; }
    public required string Autostart { get; init; }
    public required string Language { get; init; }
    public required string OpenLog { get; init; }
    public required string Exit { get; init; }

    public string State(KeeperState state) => Title + " — " + state switch
    {
        KeeperState.Pairing => StatePairing,
        KeeperState.Connected => StateConnected,
        KeeperState.Paused => StatePaused,
        _ => StateAbsent,
    };

    public string PultLearned(string address) => string.Format(CultureInfo.InvariantCulture, PultLearnedFormat, address);

    public static UiText For(UiLanguage language) => language == UiLanguage.Latvian ? Latvian : English;

    private static readonly UiText English = new()
    {
        StateAbsent = "remote not detected",
        StatePairing = "pairing",
        StateConnected = "remote connected",
        StatePaused = "paused",
        MusicMode = "The remote is in music mode. Press M + B to switch to game mode.",
        PultLearnedFormat = "Remote remembered: {0}",
        Pause = "Pause",
        Resume = "Resume",
        Forget = "Forget remote",
        Autostart = "Start with Windows",
        Language = "Language",
        OpenLog = "Open log",
        Exit = "Exit",
    };

    private static readonly UiText Latvian = new()
    {
        StateAbsent = "pults nav redzama",
        StatePairing = "notiek pārošana",
        StateConnected = "pults pievienota",
        StatePaused = "pauzēts",
        MusicMode = "Pults ir mūzikas režīmā. Nospied M + B, lai pārslēgtu uz spēļu režīmu.",
        PultLearnedFormat = "Pults iegaumēta: {0}",
        Pause = "Pauzēt",
        Resume = "Turpināt",
        Forget = "Aizmirst pulti",
        Autostart = "Startēt kopā ar Windows",
        Language = "Valoda",
        OpenLog = "Atvērt žurnālu",
        Exit = "Iziet",
    };
}

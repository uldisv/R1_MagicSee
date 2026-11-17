using Microsoft.Win32;
using R1Keeper.Core;

namespace R1Keeper;

/// <summary>
/// The interface language, kept in the per-user registry. It is not in settings.json
/// because PairingManager owns that file and rewrites it from its own copy. The installer
/// writes the first value (the language Setup ran in) and removes the key on uninstall.
/// </summary>
internal static class LanguagePreference
{
    private const string Key = @"Software\R1Keeper";
    private const string ValueName = "Language";

    public static UiLanguage Load()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key);
        return UiLanguages.Parse(key?.GetValue(ValueName) as string);
    }

    public static void Save(UiLanguage language)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key);
        key.SetValue(ValueName, UiLanguages.Code(language));
    }
}

using R1Keeper.Core;

namespace R1Keeper.Core.Tests;

public sealed class UiTextTests
{
    public static TheoryData<UiLanguage> Languages => new(UiLanguages.All);

    [Fact]
    public void Default_IsEnglish()
    {
        Assert.Equal(UiLanguage.English, UiLanguages.Default);
        Assert.Equal(UiLanguage.English, UiLanguages.Parse(null));
    }

    [Theory]
    [InlineData("en", UiLanguage.English)]
    [InlineData("EN", UiLanguage.English)]
    [InlineData("lv", UiLanguage.Latvian)]
    [InlineData("LV", UiLanguage.Latvian)]
    [InlineData(" lv ", UiLanguage.Latvian)]
    [InlineData("", UiLanguage.English)]
    [InlineData("de", UiLanguage.English)]
    [InlineData("latvian", UiLanguage.English)]
    public void Parse_UnknownFallsBackToEnglish(string code, UiLanguage expected) =>
        Assert.Equal(expected, UiLanguages.Parse(code));

    [Theory]
    [MemberData(nameof(Languages))]
    public void Code_RoundTrips(UiLanguage language) =>
        Assert.Equal(language, UiLanguages.Parse(UiLanguages.Code(language)));

    [Fact]
    public void Codes_AreTheOnesTheInstallerWrites()
    {
        Assert.Equal("en", UiLanguages.Code(UiLanguage.English));
        Assert.Equal("lv", UiLanguages.Code(UiLanguage.Latvian));
    }

    [Fact]
    public void All_ListsEveryLanguageOnceWithEnglishFirst()
    {
        Assert.Equal(Enum.GetValues<UiLanguage>().Order(), UiLanguages.All.Order());
        Assert.Equal(UiLanguage.English, UiLanguages.All[0]);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryText_IsFilledIn(UiLanguage language)
    {
        var text = UiText.For(language);
        foreach (var property in typeof(UiText).GetProperties().Where(p => p.PropertyType == typeof(string)))
            Assert.False(string.IsNullOrWhiteSpace((string?)property.GetValue(text)), $"{language}.{property.Name} is empty");
        Assert.False(string.IsNullOrWhiteSpace(UiLanguages.NativeName(language)));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void StateTexts_AreDistinct(UiLanguage language)
    {
        var text = UiText.For(language);
        var states = Enum.GetValues<KeeperState>().Select(text.State).ToList();
        Assert.All(states, s => Assert.StartsWith("R1 Keeper — ", s));
        Assert.Equal(states.Count, states.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void PultLearned_ShowsTheAddress(UiLanguage language) =>
        Assert.Contains("FF:FF:38:5D:40:01", UiText.For(language).PultLearned("FF:FF:38:5D:40:01"));

    [Fact]
    public void For_ReturnsTheRequestedLanguage()
    {
        Assert.Equal("Exit", UiText.For(UiLanguage.English).Exit);
        Assert.Equal("Iziet", UiText.For(UiLanguage.Latvian).Exit);
    }
}

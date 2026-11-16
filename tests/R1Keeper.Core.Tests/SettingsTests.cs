using R1Keeper.Core;

namespace R1Keeper.Core.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "r1keeper-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "sub", "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Address_RoundTrips()
    {
        Assert.Equal("FFFF385D4001", Settings.FormatAddress(0xFFFF385D4001));
        Assert.Equal(0xFFFF385D4001UL, Settings.ParseAddress("FFFF385D4001"));
        Assert.Equal(0xFFFF385D4001UL, Settings.ParseAddress("ffff385d4001"));
        Assert.Equal("FF:FF:38:5D:40:01", Settings.DisplayAddress(0xFFFF385D4001));
    }

    [Fact]
    public void FormatAddress_PadsToTwelveDigits()
    {
        Assert.Equal("00001A2B3C4D", Settings.FormatAddress(0x1A2B3C4D));
        Assert.Equal("00:00:1A:2B:3C:4D", Settings.DisplayAddress(0x1A2B3C4D));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("xyz")]
    [InlineData("FFFF385D40011")]
    public void ParseAddress_Invalid_ReturnsNull(string? text) => Assert.Null(Settings.ParseAddress(text));

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var s = new JsonSettingsStore(FilePath).Load();
        Assert.Null(s.PultAddress);
        Assert.Equal("Magicsee R1", s.NamePrefix);
        Assert.False(s.Paused);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{not json");
        var s = new JsonSettingsStore(FilePath).Load();
        Assert.Null(s.PultAddress);
        Assert.Equal("Magicsee R1", s.NamePrefix);
        Assert.False(s.Paused);
    }

    [Fact]
    public void Load_JsonNull_ReturnsDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "null");
        Assert.Equal("Magicsee R1", new JsonSettingsStore(FilePath).Load().NamePrefix);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new JsonSettingsStore(FilePath);
        store.Save(new Settings { PultAddress = "FFFF385D4001", Paused = true });
        var s = new JsonSettingsStore(FilePath).Load();
        Assert.Equal("FFFF385D4001", s.PultAddress);
        Assert.True(s.Paused);
        Assert.Equal("Magicsee R1", s.NamePrefix);
    }

    [Fact]
    public void Save_Twice_OverwritesPreviousFile()
    {
        var store = new JsonSettingsStore(FilePath);
        store.Save(new Settings { PultAddress = "FFFF385D4001" });
        store.Save(new Settings { PultAddress = null, Paused = true });
        var s = store.Load();
        Assert.Null(s.PultAddress);
        Assert.True(s.Paused);
    }
}

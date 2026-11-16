using R1Keeper.Core;

namespace R1Keeper.Core.Tests;

public sealed class AutostartPathTests
{
    private const string Here = @"C:\Tools\R1Keeper\R1Keeper.exe";
    private const string Old = @"D:\Old\R1Keeper.exe";

    private static Func<string, bool> Existing(params string[] files) =>
        path => files.Contains(path, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void AutostartOff_NothingToRepair() =>
        Assert.Null(AutostartPath.Repaired(null, Here, Existing(Here)));

    [Fact]
    public void RegisteredFileGone_PointsToThisCopy() =>
        Assert.Equal("\"" + Here + "\"", AutostartPath.Repaired("\"" + Old + "\"", Here, Existing(Here)));

    [Fact]
    public void RegisteredFileStillExists_IsLeftAlone() =>
        // e.g. a debug build is run while the installed copy is registered
        Assert.Null(AutostartPath.Repaired("\"" + Old + "\"", Here, Existing(Here, Old)));

    [Fact]
    public void AlreadyPointsHere_NoChange() =>
        Assert.Null(AutostartPath.Repaired("\"" + Here + "\"", Here, Existing(Here)));

    [Theory]
    [InlineData(null)]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe")]
    public void NotRunningAsR1KeeperExe_NeverRegistersItself(string? processPath) =>
        Assert.Null(AutostartPath.Repaired("\"" + Old + "\"", processPath, Existing()));

    [Fact]
    public void UnquotedRegisteredPath_IsUnderstood() =>
        Assert.Null(AutostartPath.Repaired(Old, Here, Existing(Here, Old)));

    [Fact]
    public void Command_QuotesThePath() => Assert.Equal("\"" + Here + "\"", AutostartPath.Command(Here));
}

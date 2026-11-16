using System.Text.RegularExpressions;
using R1Keeper.Core;

namespace R1Keeper.Core.Tests;

public sealed class FileLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "r1keeper-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "r1keeper.log");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Write_AppendsTimestampedLine()
    {
        var log = new FileLog(FilePath);
        log.Write("first");
        log.Write("second");
        var lines = File.ReadAllLines(FilePath);
        Assert.Equal(2, lines.Length);
        Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}  first$"), lines[0]);
        Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}  second$"), lines[1]);
    }

    [Fact]
    public void Write_OverLimit_RotatesToDot1()
    {
        var log = new FileLog(FilePath, maxBytes: 200);
        for (int i = 0; i < 20; i++) log.Write("line number " + i);
        Assert.True(File.Exists(FilePath + ".1"));
        Assert.True(new FileInfo(FilePath).Length < 200 + 60); // at most one line past the limit
        Assert.Contains("line number 19", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Write_UnwritablePath_DoesNotThrow()
    {
        var log = new FileLog(@"Q:\nope\x.log");
        log.Write("ignored");
    }

    [Fact]
    public void Write_FromManyThreads_KeepsEveryLine()
    {
        var log = new FileLog(FilePath);
        Parallel.For(0, 200, i => log.Write("msg " + i));
        Assert.Equal(200, File.ReadAllLines(FilePath).Length);
    }
}

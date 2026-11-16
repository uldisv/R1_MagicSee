using R1Keeper.Core;

namespace R1Keeper.Core.Tests;

public sealed class ModeReminderTests
{
    private static (ModeReminder reminder, Func<int> fired) Create()
    {
        var reminder = new ModeReminder();
        int count = 0;
        reminder.MusicModeDetected += () => count++;
        return (reminder, () => count);
    }

    [Fact]
    public void KeyPressed_FiresOnce()
    {
        var (r, fired) = Create();
        r.OnConnected();
        r.OnConsumerReport([0x02, 0x01, 0x00]);   // volume up
        Assert.Equal(1, fired());
        r.OnConsumerReport([0x02, 0x02, 0x00]);   // volume down
        Assert.Equal(1, fired());
    }

    [Fact]
    public void AllZeroReport_DoesNotFire()
    {
        var (r, fired) = Create();
        r.OnConnected();
        r.OnConsumerReport([0x02, 0x00, 0x00]);
        Assert.Equal(0, fired());
    }

    [Fact]
    public void HighByteKey_Fires()
    {
        var (r, fired) = Create();
        r.OnConnected();
        r.OnConsumerReport([0x02, 0x00, 0x01]);
        Assert.Equal(1, fired());
    }

    [Fact]
    public void Reconnect_AllowsAnotherReminder()
    {
        var (r, fired) = Create();
        r.OnConnected();
        r.OnConsumerReport([0x02, 0x01, 0x00]);
        r.OnConnected();
        r.OnConsumerReport([0x02, 0x20, 0x00]);   // next track
        Assert.Equal(2, fired());
    }

    [Fact]
    public void BeforeFirstConnected_DoesNotFire()
    {
        var (r, fired) = Create();
        r.OnConsumerReport([0x02, 0x01, 0x00]);
        Assert.Equal(0, fired());
    }

    [Fact]
    public void ShortReport_DoesNotThrow()
    {
        var (r, fired) = Create();
        r.OnConnected();
        r.OnConsumerReport([]);
        r.OnConsumerReport([0x02]);
        Assert.Equal(0, fired());
    }
}

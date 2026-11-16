using R1Keeper.Core;

namespace R1Keeper.Core.Tests;

/// <summary>Findings from the whole-branch review.</summary>
public sealed class PairingManagerRobustnessTests
{
    /// <summary>The pult advertises unpaired and gets paired; then the link dies while the pairing stays.</summary>
    private static async Task PairThenLoseLink(Rig rig)
    {
        await rig.Advertise();
        Assert.Equal(KeeperState.Connected, rig.Manager.State);
        rig.Bt.Connected = false;
    }

    private static int Count(Rig rig, string call) => rig.Bt.Calls.Count(c => c == call);

    [Fact]
    public async Task PairingThatDoesNotHold_BacksOffInsteadOfLoopingForever()
    {
        var rig = Rig.WithSavedPult();

        // three rounds of "pairs fine, link dies at once": tolerated without a pause
        for (int round = 0; round < 3; round++)
        {
            await PairThenLoseLink(rig);
            await rig.Advertise();                       // stale pairing removed
        }
        Assert.Equal(3, Count(rig, "pair"));
        Assert.Equal(3, Count(rig, "unpair"));

        // the fourth time the program must pause instead of cycling again
        await PairThenLoseLink(rig);
        await rig.Advertise();
        Assert.Equal(3, Count(rig, "unpair"));
        Assert.Equal(KeeperState.Absent, rig.Manager.State);

        rig.Clock.Advance(TimeSpan.FromSeconds(5));
        await rig.Advertise();
        Assert.Equal(4, Count(rig, "unpair"));
    }

    [Fact]
    public async Task PairingThatHeld_IsNotCountedAsFailure()
    {
        var rig = Rig.WithSavedPult();
        for (int round = 0; round < 3; round++)
        {
            await PairThenLoseLink(rig);
            await rig.Advertise();
        }

        // this pairing holds for half a minute before the pult goes away: a normal session
        await rig.Advertise();
        rig.Clock.Advance(TimeSpan.FromSeconds(30));
        rig.Bt.Connected = false;
        await rig.Advertise();
        Assert.Equal(4, Count(rig, "unpair"));           // acted at once, no pause

        // and the count started again from zero
        for (int round = 0; round < 3; round++)
        {
            await PairThenLoseLink(rig);
            await rig.Advertise();
        }
        Assert.Equal(7, Count(rig, "unpair"));
    }

    [Fact]
    public async Task HungPairing_TimesOut_CountsAsFailure_AndLaterAdvertisementsAreHandled()
    {
        var rig = Rig.WithSavedPult(operationTimeout: TimeSpan.FromMilliseconds(50));
        rig.Bt.PairGate = new TaskCompletionSource();    // never completes

        await rig.Advertise().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(KeeperState.Absent, rig.Manager.State);
        Assert.Contains(rig.Log, m => m.Contains("threw"));

        rig.Bt.PairGate = null;
        await rig.Advertise();
        Assert.Equal(KeeperState.Connected, rig.Manager.State);
    }

    [Fact]
    public async Task HungUnpair_DoesNotBlockPauseForever()
    {
        var rig = Rig.WithSavedPult(operationTimeout: TimeSpan.FromMilliseconds(50));
        rig.Bt.Paired = true;
        rig.Bt.UnpairGate = new TaskCompletionSource();  // never completes

        await rig.Manager.PauseAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(KeeperState.Paused, rig.Manager.State);
    }
}

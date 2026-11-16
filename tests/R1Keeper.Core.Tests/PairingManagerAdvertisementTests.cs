using R1Keeper.Core;

namespace R1Keeper.Core.Tests;

public sealed class PairingManagerAdvertisementTests
{
    [Fact]
    public async Task Learns_FirstMatchingName_SavesAddressAndRaisesEvent()
    {
        var rig = new Rig();
        await rig.Advertise();
        Assert.Equal("FFFF385D4001", rig.Store.Settings.PultAddress);
        Assert.True(rig.Store.SaveCount >= 1);
        Assert.Equal([Rig.R1], rig.Learned);
        Assert.Equal(Rig.R1, rig.Manager.PultAddress);
    }

    [Fact]
    public async Task Ignores_OtherName_WhenNoAddressSaved()
    {
        var rig = new Rig();
        await rig.Advertise(0x123456789ABC, "OnePlus 13");
        Assert.Empty(rig.Bt.Calls);
        Assert.Null(rig.Store.Settings.PultAddress);
        Assert.Empty(rig.Learned);
    }

    [Fact]
    public async Task Ignores_SameNameDifferentAddress_WhenAddressSaved()
    {
        var rig = Rig.WithSavedPult();
        await rig.Advertise(0x111111111111, "Magicsee R1");
        Assert.Empty(rig.Bt.Calls);
        Assert.Equal("FFFF385D4001", rig.Store.Settings.PultAddress);
        Assert.Equal(KeeperState.Absent, rig.Manager.State);
    }

    [Fact]
    public async Task SavedAddress_IsHandled_EvenWithoutName()
    {
        var rig = Rig.WithSavedPult();
        await rig.Advertise(Rig.R1, "");
        Assert.Equal(["pair", "watch"], rig.Bt.Calls);
    }

    [Fact]
    public async Task Unpaired_Pairs_AndBecomesConnected()
    {
        var rig = new Rig();
        await rig.Advertise();
        Assert.Equal(["pair", "watch"], rig.Bt.Calls);
        Assert.Equal(KeeperState.Connected, rig.Manager.State);
    }

    [Fact]
    public async Task Paired_UnpairsOnly_ThenPairsOnNextAdvertisement()
    {
        var rig = Rig.WithSavedPult();
        rig.Bt.Paired = true;

        await rig.Advertise();
        Assert.Equal(["unpair"], rig.Bt.Calls);
        Assert.Equal(KeeperState.Pairing, rig.Manager.State);

        await rig.Advertise();
        Assert.Equal(["unpair", "pair", "watch"], rig.Bt.Calls);
        Assert.Equal(KeeperState.Connected, rig.Manager.State);
    }

    [Fact]
    public async Task PairFails_RetriesThreeTimesWithoutDelay_ThenBacksOff()
    {
        var rig = Rig.WithSavedPult();
        foreach (var r in new[] { false, false, false, false, true }) rig.Bt.PairResults.Enqueue(r);

        for (int i = 0; i < 4; i++) await rig.Advertise();
        Assert.Equal(4, rig.Bt.Calls.Count(c => c == "pair"));
        Assert.Equal(KeeperState.Absent, rig.Manager.State);

        await rig.Advertise();                          // inside the 5 s back-off
        Assert.Equal(4, rig.Bt.Calls.Count(c => c == "pair"));

        rig.Clock.Advance(TimeSpan.FromSeconds(5));
        await rig.Advertise();
        Assert.Equal(5, rig.Bt.Calls.Count(c => c == "pair"));
        Assert.Equal(KeeperState.Connected, rig.Manager.State);
    }

    [Fact]
    public async Task PairingThatHeld_ResetsFailureCount()
    {
        var rig = Rig.WithSavedPult();
        foreach (var r in new[] { false, false, false, true }) rig.Bt.PairResults.Enqueue(r);
        for (int i = 0; i < 4; i++) await rig.Advertise();
        Assert.Equal(KeeperState.Connected, rig.Manager.State);

        rig.Clock.Advance(TimeSpan.FromSeconds(30));    // the pairing held, so it counts as a success
        rig.Bt.Paired = false;
        rig.Bt.Connected = false;
        rig.Bt.PairResults.Enqueue(false);
        rig.Bt.PairResults.Enqueue(false);
        await rig.Advertise();
        await rig.Advertise();                          // no clock change: the counter restarted, so no back-off yet
        Assert.Equal(6, rig.Bt.Calls.Count(c => c == "pair"));
    }

    [Fact]
    public async Task UnpairFails_CountsAsFailure()
    {
        var rig = Rig.WithSavedPult();
        rig.Bt.Paired = true;
        rig.Bt.UnpairResult = false;

        for (int i = 0; i < 4; i++) await rig.Advertise();
        Assert.Equal(4, rig.Bt.Calls.Count(c => c == "unpair"));
        Assert.Equal(KeeperState.Absent, rig.Manager.State);

        await rig.Advertise();
        Assert.Equal(4, rig.Bt.Calls.Count);
    }

    [Fact]
    public async Task PairThrows_CountsAsFailure_AndNextAdvertisementIsHandled()
    {
        var rig = Rig.WithSavedPult();
        rig.Bt.ThrowOnPair = new InvalidOperationException("boom");

        await rig.Advertise();                          // must not throw
        Assert.Equal(KeeperState.Absent, rig.Manager.State);
        Assert.Contains(rig.Log, m => m.Contains("boom"));

        rig.Bt.ThrowOnPair = null;
        await rig.Advertise();
        Assert.Equal(KeeperState.Connected, rig.Manager.State);
    }

    [Fact]
    public async Task AdvertisementWhileBusy_IsDropped()
    {
        var rig = Rig.WithSavedPult();
        rig.Bt.PairGate = new TaskCompletionSource();

        var first = rig.Advertise();
        Assert.False(first.IsCompleted);

        var second = rig.Advertise();
        Assert.True(second.IsCompleted);

        rig.Bt.PairGate.SetResult();
        await first;
        Assert.Equal(1, rig.Bt.Calls.Count(c => c == "pair"));
        Assert.Equal(KeeperState.Connected, rig.Manager.State);
    }

    [Fact]
    public async Task PairedStateChangedExternally_IsRespected()
    {
        var rig = Rig.WithSavedPult();
        await rig.Advertise();
        Assert.Equal(KeeperState.Connected, rig.Manager.State);

        // the user deleted the pult in Windows Bluetooth settings behind our back
        rig.Bt.Paired = false;
        rig.Bt.Connected = false;
        rig.Bt.Calls.Clear();
        await rig.Advertise();
        Assert.Equal(["pair", "watch"], rig.Bt.Calls);
    }

    [Fact]
    public async Task StateChanged_FiresOnlyOnChange()
    {
        var rig = Rig.WithSavedPult();
        await rig.Advertise();
        Assert.Equal([KeeperState.Pairing, KeeperState.Connected], rig.States);
    }

    [Fact]
    public async Task Paused_IgnoresAdvertisements()
    {
        var rig = Rig.WithSavedPult(s => s.Paused = true);
        Assert.Equal(KeeperState.Paused, rig.Manager.State);
        await rig.Advertise();
        Assert.Empty(rig.Bt.Calls);
    }

    [Fact]
    public async Task OnlyTheSavedAddressIsEverActedOn()
    {
        var rig = new Rig();
        await rig.Advertise();                                   // learns R1
        await rig.Advertise(0x222222222222, "Magicsee R1");      // a second unit nearby
        rig.Bt.Paired = false;
        await rig.Advertise(0x222222222222, "Magicsee R1");
        Assert.All(rig.Bt.CallAddresses, a => Assert.Equal(Rig.R1, a));
    }
}

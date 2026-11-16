using R1Keeper.Core;

namespace R1Keeper.Core.Tests;

public sealed class PairingManagerLifecycleTests
{
    private static async Task<Rig> ConnectedRig()
    {
        var rig = Rig.WithSavedPult();
        rig.Bt.Paired = true;
        rig.Bt.Connected = true;
        await rig.Manager.StartAsync();
        rig.Bt.Calls.Clear();
        rig.Bt.CallAddresses.Clear();
        return rig;
    }

    [Fact]
    public async Task Start_PairedAndConnected_IsConnected_AndWatchesLink()
    {
        var rig = Rig.WithSavedPult();
        rig.Bt.Paired = true;
        rig.Bt.Connected = true;
        await rig.Manager.StartAsync();
        Assert.Equal(["watch"], rig.Bt.Calls);
        Assert.Equal(KeeperState.Connected, rig.Manager.State);
        Assert.True(rig.Bt.Started);
    }

    [Fact]
    public async Task Start_PairedButNotConnected_IsAbsent()
    {
        var rig = Rig.WithSavedPult();
        rig.Bt.Paired = true;
        await rig.Manager.StartAsync();
        Assert.Empty(rig.Bt.Calls);
        Assert.Equal(KeeperState.Absent, rig.Manager.State);
    }

    [Fact]
    public async Task Start_NoAddress_IsAbsent()
    {
        var rig = new Rig();
        await rig.Manager.StartAsync();
        Assert.Equal(KeeperState.Absent, rig.Manager.State);
        Assert.True(rig.Bt.Started);
    }

    [Fact]
    public async Task Start_PausedSetting_IsPaused()
    {
        var rig = Rig.WithSavedPult(s => s.Paused = true);
        rig.Bt.Paired = true;
        rig.Bt.Connected = true;
        await rig.Manager.StartAsync();
        Assert.Equal(KeeperState.Paused, rig.Manager.State);
        Assert.Empty(rig.Bt.Calls);
    }

    [Fact]
    public async Task Start_BluetoothThrows_DoesNotThrow_StateAbsent()
    {
        var rig = Rig.WithSavedPult();
        rig.Bt.ThrowOnStart = new InvalidOperationException("radio off");
        await rig.Manager.StartAsync();
        Assert.Equal(KeeperState.Absent, rig.Manager.State);
        Assert.Contains(rig.Log, m => m.Contains("radio off"));
    }

    [Fact]
    public async Task Start_SubscribesEvents()
    {
        var rig = Rig.WithSavedPult();
        await rig.Manager.StartAsync();
        rig.Bt.RaiseAdvertisement(Rig.R1, Rig.R1Name);
        Assert.Contains("pair", rig.Bt.Calls);

        rig.Bt.Connected = false;
        rig.Bt.RaiseLinkLost(Rig.R1);
        Assert.Equal("unpair", rig.Bt.Calls[^1]);
    }

    [Fact]
    public async Task LinkLost_WhilePaired_Unpairs_AndBecomesAbsent()
    {
        var rig = await ConnectedRig();
        rig.Bt.Connected = false;
        await rig.Manager.HandleLinkLostAsync(Rig.R1);
        Assert.Equal(["unpair"], rig.Bt.Calls);
        Assert.Equal(KeeperState.Absent, rig.Manager.State);
    }

    [Fact]
    public async Task LinkLost_Late_WhenLinkIsAlive_DoesNothing()
    {
        // a link-lost event that was queued before a fresh re-pair must not undo that pairing
        var rig = await ConnectedRig();
        await rig.Manager.HandleLinkLostAsync(Rig.R1);
        Assert.Empty(rig.Bt.Calls);
        Assert.Equal(KeeperState.Connected, rig.Manager.State);
    }

    [Fact]
    public async Task LinkLost_OtherAddress_DoesNothing()
    {
        var rig = await ConnectedRig();
        rig.Bt.Connected = false;
        await rig.Manager.HandleLinkLostAsync(0x111111111111);
        Assert.Empty(rig.Bt.Calls);
        Assert.Equal(KeeperState.Connected, rig.Manager.State);
    }

    [Fact]
    public async Task LinkLost_ThenAdvertisement_PairsDirectly()
    {
        var rig = await ConnectedRig();
        rig.Bt.Connected = false;
        await rig.Manager.HandleLinkLostAsync(Rig.R1);
        await rig.Advertise();
        Assert.Equal(["unpair", "pair", "watch"], rig.Bt.Calls);
        Assert.Equal(KeeperState.Connected, rig.Manager.State);
    }

    [Fact]
    public async Task LinkLost_UnpairFails_BecomesAbsent()
    {
        var rig = await ConnectedRig();
        rig.Bt.Connected = false;
        rig.Bt.UnpairResult = false;
        await rig.Manager.HandleLinkLostAsync(Rig.R1);
        Assert.Equal(KeeperState.Absent, rig.Manager.State);
    }

    [Fact]
    public async Task Pause_UnpairsAndPersists_AdvertisementsIgnored()
    {
        var rig = await ConnectedRig();
        await rig.Manager.PauseAsync();
        Assert.Equal(["unpair"], rig.Bt.Calls);
        Assert.True(rig.Store.Settings.Paused);
        Assert.Equal(KeeperState.Paused, rig.Manager.State);

        await rig.Advertise();
        Assert.Equal(["unpair"], rig.Bt.Calls);
    }

    [Fact]
    public async Task Pause_WhenNotPaired_DoesNotUnpair()
    {
        var rig = Rig.WithSavedPult();
        await rig.Manager.StartAsync();
        await rig.Manager.PauseAsync();
        Assert.Empty(rig.Bt.Calls);
        Assert.Equal(KeeperState.Paused, rig.Manager.State);
    }

    [Fact]
    public async Task Pause_UnpairFails_StillPauses()
    {
        var rig = await ConnectedRig();
        rig.Bt.UnpairResult = false;
        await rig.Manager.PauseAsync();
        Assert.True(rig.Store.Settings.Paused);
        Assert.Equal(KeeperState.Paused, rig.Manager.State);
    }

    [Fact]
    public async Task Paused_LinkLost_DoesNothing()
    {
        var rig = Rig.WithSavedPult(s => s.Paused = true);
        rig.Bt.Paired = true;
        await rig.Manager.StartAsync();
        await rig.Manager.HandleLinkLostAsync(Rig.R1);
        Assert.Empty(rig.Bt.Calls);
    }

    [Fact]
    public async Task Resume_ClearsPaused_AdvertisementPairsAgain()
    {
        var rig = await ConnectedRig();
        await rig.Manager.PauseAsync();
        await rig.Manager.ResumeAsync();
        Assert.False(rig.Store.Settings.Paused);
        Assert.Equal(KeeperState.Absent, rig.Manager.State);

        await rig.Advertise();
        Assert.Equal(["unpair", "pair", "watch"], rig.Bt.Calls);
        Assert.Equal(KeeperState.Connected, rig.Manager.State);
    }

    [Fact]
    public async Task Resume_ClearsBackoff()
    {
        var rig = Rig.WithSavedPult();
        for (int i = 0; i < 4; i++) rig.Bt.PairResults.Enqueue(false);
        for (int i = 0; i < 4; i++) await rig.Advertise();   // now inside the 5 s back-off

        await rig.Manager.PauseAsync();
        await rig.Manager.ResumeAsync();
        await rig.Advertise();
        Assert.Equal(KeeperState.Connected, rig.Manager.State);
    }

    [Fact]
    public async Task Forget_UnpairsAndClearsAddress_NextMatchingNameIsLearned()
    {
        var rig = await ConnectedRig();
        await rig.Manager.ForgetPultAsync();
        Assert.Equal(["unpair"], rig.Bt.Calls);
        Assert.Null(rig.Store.Settings.PultAddress);
        Assert.Null(rig.Manager.PultAddress);
        Assert.Equal(KeeperState.Absent, rig.Manager.State);

        await rig.Advertise(0x222222222222, "Magicsee R1");
        Assert.Equal("222222222222", rig.Store.Settings.PultAddress);
        Assert.Equal([0x222222222222UL], rig.Learned);
    }

    [Fact]
    public async Task Forget_WhilePaused_StaysPaused()
    {
        var rig = Rig.WithSavedPult(s => s.Paused = true);
        await rig.Manager.StartAsync();
        await rig.Manager.ForgetPultAsync();
        Assert.Null(rig.Store.Settings.PultAddress);
        Assert.Equal(KeeperState.Paused, rig.Manager.State);
    }

    [Fact]
    public async Task Forget_NeverTouchesOtherDevices()
    {
        var rig = await ConnectedRig();
        await rig.Manager.ForgetPultAsync();
        Assert.NotEmpty(rig.Bt.CallAddresses);
        Assert.All(rig.Bt.CallAddresses, a => Assert.Equal(Rig.R1, a));
    }

    [Fact]
    public async Task Forget_WithNoPult_DoesNothing()
    {
        var rig = new Rig();
        await rig.Manager.StartAsync();
        await rig.Manager.ForgetPultAsync();
        Assert.Empty(rig.Bt.Calls);
        Assert.Equal(KeeperState.Absent, rig.Manager.State);
    }
}

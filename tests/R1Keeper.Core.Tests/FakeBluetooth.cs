using R1Keeper.Core;

namespace R1Keeper.Core.Tests;

/// <summary>Scriptable stand-in for the real Bluetooth layer. Records every state-changing call.</summary>
internal sealed class FakeBluetooth : IPultBluetooth
{
    public event Action<PultAdvertisement>? AdvertisementReceived;
    public event Action<ulong>? LinkLost;

    public bool Paired;
    public bool Connected;
    public bool UnpairResult = true;
    public Queue<bool> PairResults = new();          // empty = pairing succeeds
    public Exception? ThrowOnPair;
    public Exception? ThrowOnStart { get; set; }
    public TaskCompletionSource? PairGate;           // when set, PairAsync waits for it
    public bool Started;

    public List<string> Calls = new();               // "unpair", "pair", "watch"
    public List<ulong> CallAddresses = new();        // address of each entry in Calls

    private void Record(string call, ulong address)
    {
        lock (Calls) { Calls.Add(call); CallAddresses.Add(address); }
    }

    public void Start()
    {
        if (ThrowOnStart is not null) throw ThrowOnStart;
        Started = true;
    }

    public void Stop() => Started = false;

    public Task<bool> IsPairedAsync(ulong address) => Task.FromResult(Paired);

    public Task<bool> IsConnectedAsync(ulong address) => Task.FromResult(Connected);

    public TaskCompletionSource? UnpairGate { get; set; }   // when set, UnpairAsync waits for it

    public async Task<bool> UnpairAsync(ulong address)
    {
        Record("unpair", address);
        if (UnpairGate is not null) await UnpairGate.Task;
        if (UnpairResult) { Paired = false; Connected = false; }
        return UnpairResult;
    }

    public async Task<bool> PairAsync(ulong address)
    {
        Record("pair", address);
        if (PairGate is not null) await PairGate.Task;
        if (ThrowOnPair is not null) throw ThrowOnPair;
        bool ok = PairResults.Count == 0 || PairResults.Dequeue();
        if (ok) { Paired = true; Connected = true; }
        return ok;
    }

    public void WatchLink(ulong address) => Record("watch", address);

    public void RaiseAdvertisement(ulong address, string name) => AdvertisementReceived?.Invoke(new PultAdvertisement(address, name));

    public void RaiseLinkLost(ulong address) => LinkLost?.Invoke(address);
}

internal sealed class InMemorySettingsStore : ISettingsStore
{
    public Settings Settings = new();
    public int SaveCount;

    public Settings Load() => Settings;

    public void Save(Settings settings)
    {
        Settings = settings;
        SaveCount++;
    }
}

internal sealed class FakeClock
{
    public DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>A manager wired to fakes, plus the fakes.</summary>
internal sealed class Rig
{
    public const ulong R1 = 0xFFFF385D4001;
    public const string R1Name = "Magicsee R1       ";

    public readonly FakeBluetooth Bt = new();
    public readonly InMemorySettingsStore Store = new();
    public readonly FakeClock Clock = new();
    public readonly List<string> Log = new();
    public readonly List<KeeperState> States = new();
    public readonly List<ulong> Learned = new();
    public readonly PairingManager Manager;

    public Rig(Action<Settings>? configure = null, TimeSpan? operationTimeout = null)
    {
        configure?.Invoke(Store.Settings);
        Manager = new PairingManager(Bt, Store, new RetryPolicy(), () => Clock.Now, m => { lock (Log) Log.Add(m); }, operationTimeout);
        Manager.StateChanged += s => States.Add(s);
        Manager.PultLearned += a => Learned.Add(a);
    }

    /// <summary>A rig whose saved pult is the R1.</summary>
    public static Rig WithSavedPult(Action<Settings>? configure = null, TimeSpan? operationTimeout = null) =>
        new(s => { s.PultAddress = "FFFF385D4001"; configure?.Invoke(s); }, operationTimeout);

    public Task Advertise(ulong address = R1, string name = R1Name) =>
        Manager.HandleAdvertisementAsync(new PultAdvertisement(address, name));
}

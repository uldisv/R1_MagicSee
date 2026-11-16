namespace R1Keeper.Core;

public enum KeeperState
{
    /// <summary>The pult is not in a working connection.</summary>
    Absent,
    /// <summary>The old pairing is being removed or a new one made.</summary>
    Pairing,
    Connected,
    /// <summary>The user paused the program; advertisements are ignored.</summary>
    Paused,
}

/// <summary>
/// Decides when to remove the pult's pairing and when to pair it again.
///
/// Why this exists: with a stored pairing, the Magicsee R1 re-sends its pairing keys on
/// every reconnect and Windows drops the link, so the pult only works on a fresh pairing.
/// A pult that is advertising is not in a working connection, so an advertisement is the
/// signal to act. Only one operation runs at a time, and only the saved address is ever
/// acted on.
/// </summary>
public sealed class PairingManager
{
    private readonly IPultBluetooth _bt;
    private readonly ISettingsStore _store;
    private readonly RetryPolicy _retry;
    private readonly Func<DateTimeOffset> _now;
    private readonly Action<string> _log;
    private readonly Settings _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private ulong? _address;
    private int _failures;
    private DateTimeOffset _notBefore = DateTimeOffset.MinValue;
    private volatile KeeperState _state;
    private bool _subscribed;

    // A pairing only counts as a success once it has held for a while. Without this, a
    // pairing that succeeds and then drops at once (weak battery, edge of range, a Windows
    // build where a fresh pairing does not stick) would cycle unpair/pair forever, because
    // every round looks like a success.
    private static readonly TimeSpan PairingMustHold = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxAdvertisementAge = TimeSpan.FromSeconds(3);
    private readonly TimeSpan _operationTimeout;
    private DateTimeOffset _lastPairSuccess;
    private bool _pairingUnproven;

    public PairingManager(IPultBluetooth bt, ISettingsStore store, RetryPolicy retry, Func<DateTimeOffset> now, Action<string> log, TimeSpan? operationTimeout = null)
    {
        _bt = bt;
        _store = store;
        _retry = retry;
        _now = now;
        _log = log;
        _operationTimeout = operationTimeout ?? DefaultOperationTimeout;
        _settings = store.Load();
        _address = Settings.ParseAddress(_settings.PultAddress);
        _state = _settings.Paused ? KeeperState.Paused : KeeperState.Absent;
    }

    public KeeperState State => _state;

    /// <summary>The one device this program may act on; null until a pult has been learned.</summary>
    public ulong? PultAddress => _address;

    /// <summary>Raised only when the state actually changes.</summary>
    public event Action<KeeperState>? StateChanged;

    public event Action<ulong>? PultLearned;

    /// <summary>Works out the starting state, subscribes to Bluetooth events and starts listening.</summary>
    public async Task StartAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_state != KeeperState.Paused && _address is { } address)
            {
                try
                {
                    if (await Bounded(_bt.IsPairedAsync(address)) &&
                        await Bounded(_bt.IsConnectedAsync(address)))
                    {
                        // If this pairing is in fact stale, the pult will advertise and the
                        // advertisement rule takes over.
                        _bt.WatchLink(address);
                        SetState(KeeperState.Connected);
                    }
                }
                catch (Exception e)
                {
                    _log("reading the starting state failed: " + e.Message);
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        if (!_subscribed)
        {
            _subscribed = true;
            _bt.AdvertisementReceived += adv => _ = Guarded(() => HandleAdvertisementAsync(adv));
            _bt.LinkLost += address => _ = Guarded(() => HandleLinkLostAsync(address));
        }

        try
        {
            _bt.Start();
        }
        catch (Exception e)
        {
            _log("starting Bluetooth failed: " + e.Message);
        }
    }

    /// <summary>
    /// The link dropped. Remove the pairing straight away so the next wake-up starts from
    /// the fast path and Windows does not spin in its failing reconnect loop.
    /// </summary>
    public async Task HandleLinkLostAsync(ulong address)
    {
        if (_state == KeeperState.Paused || address != _address) return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_state == KeeperState.Paused || address != _address) return;
            bool connected = true;
            try
            {
                connected = await Bounded(_bt.IsConnectedAsync(address));
                // A live link means this event is late: it was queued before a fresh
                // re-pair. Leave that pairing alone.
                if (!connected && await Bounded(_bt.IsPairedAsync(address)))
                {
                    bool removed = await Bounded(_bt.UnpairAsync(address));
                    _log(removed ? "link lost; pairing removed" : "link lost; removing the pairing failed");
                }
            }
            catch (Exception e)
            {
                connected = false;
                _log("handling the lost link threw: " + e.Message);
            }

            if (!connected && _state == KeeperState.Connected) SetState(KeeperState.Absent);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Stops all automation and removes the pult's pairing, so the pult can be used with
    /// another device (otherwise Windows keeps reconnecting to it every second).
    /// </summary>
    public async Task PauseAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await UnpairIfPairedAsync("pause").ConfigureAwait(false);
            _settings.Paused = true;
            _store.Save(_settings);
            SetState(KeeperState.Paused);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResumeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _settings.Paused = false;
            _store.Save(_settings);
            _failures = 0;
            _pairingUnproven = false;
            _notBefore = DateTimeOffset.MinValue;
            SetState(KeeperState.Absent);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Removes the pairing and forgets the address; the next matching pult seen is learned.</summary>
    public async Task ForgetPultAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_address is null) return;
            await UnpairIfPairedAsync("forget").ConfigureAwait(false);
            _address = null;
            _settings.PultAddress = null;
            _store.Save(_settings);
            _failures = 0;
            _pairingUnproven = false;
            _notBefore = DateTimeOffset.MinValue;
            _log("pult forgotten");
            SetState(_settings.Paused ? KeeperState.Paused : KeeperState.Absent);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task UnpairIfPairedAsync(string why)
    {
        if (_address is not { } address) return;
        try
        {
            if (await Bounded(_bt.IsPairedAsync(address)))
            {
                bool removed = await Bounded(_bt.UnpairAsync(address));
                _log(removed ? $"{why}: pairing removed" : $"{why}: removing the pairing failed");
            }
        }
        catch (Exception e)
        {
            _log($"{why}: removing the pairing threw: {e.Message}");
        }
    }

    private async Task Guarded(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _log("unexpected error: " + e);
        }
    }

    public async Task HandleAdvertisementAsync(PultAdvertisement adv)
    {
        if (_state == KeeperState.Paused) return;

        // An advertisement that was received a while ago says nothing about the pult now.
        if (adv.Timestamp != default && _now() - adv.Timestamp > MaxAdvertisementAge) return;

        // Cheap filters before taking the lock: the air is full of other devices.
        var known = _address;
        if (known is not null)
        {
            if (adv.Address != known) return;
        }
        else if (!adv.LocalName.StartsWith(_settings.NamePrefix, StringComparison.Ordinal))
        {
            return;
        }

        if (!_gate.Wait(0)) return; // busy: this advertisement is dropped, the next one will do
        try
        {
            if (_state == KeeperState.Paused) return;
            if (_address is null)
            {
                Learn(adv.Address);
            }
            else if (adv.Address != _address)
            {
                return;
            }

            if (_now() < _notBefore) return;
            ulong address = _address!.Value;

            try
            {
                bool paired = await Bounded(_bt.IsPairedAsync(address));
                if (paired && await Bounded(_bt.IsConnectedAsync(address)))
                {
                    // An advertisement, yet the link is up. Windows does this: about a minute
                    // after pairing it delivers an advertisement for the pult although the
                    // pult is connected and silent on the air. The pairing is healthy.
                    string age = adv.Timestamp == default ? "" : $" (received {(_now() - adv.Timestamp).TotalSeconds:F1} s ago)";
                    _log("advertisement ignored: the link is up" + age);
                    if (_state != KeeperState.Connected)
                    {
                        _bt.WatchLink(address);
                        SetState(KeeperState.Connected);
                    }
                    return;
                }

                // The pult really is out of a working connection. Did the last pairing hold
                // long enough to count as a success?
                if (_pairingUnproven)
                {
                    _pairingUnproven = false;
                    if (_now() - _lastPairSuccess >= PairingMustHold)
                    {
                        _failures = 0;
                    }
                    else if (Fail("the last pairing did not hold") > TimeSpan.Zero)
                    {
                        return;
                    }
                }

                if (paired)
                {
                    // A pairing exists but the pult is advertising, so the pairing is stale.
                    // Remove it; pairing happens on the next advertisement.
                    SetState(KeeperState.Pairing);
                    if (await Bounded(_bt.UnpairAsync(address)))
                    {
                        _log("old pairing removed; pairing on the next advertisement");
                    }
                    else
                    {
                        Fail("removing the old pairing failed");
                    }
                    return;
                }

                SetState(KeeperState.Pairing);
                if (await Bounded(_bt.PairAsync(address)))
                {
                    // _failures is cleared later, once this pairing has proven it holds
                    _lastPairSuccess = _now();
                    _pairingUnproven = true;
                    _notBefore = DateTimeOffset.MinValue;
                    _bt.WatchLink(address);
                    _log("paired");
                    SetState(KeeperState.Connected);
                }
                else
                {
                    Fail("pairing failed");
                }
            }
            catch (Exception e)
            {
                Fail("operation threw: " + e.Message);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Learn(ulong address)
    {
        _address = address;
        _settings.PultAddress = Settings.FormatAddress(address);
        _store.Save(_settings);
        _log("pult learned: " + Settings.DisplayAddress(address));
        PultLearned?.Invoke(address);
    }

    /// <returns>How long the program now waits before acting again (zero for the free retries).</returns>
    private TimeSpan Fail(string what)
    {
        _failures++;
        var delay = _retry.DelayAfterFailure(_failures);
        _notBefore = _now() + delay;
        _log($"{what} (failure {_failures} in a row, next attempt in {delay.TotalSeconds:0} s)");
        SetState(KeeperState.Absent);
        return delay;
    }

    /// <summary>
    /// Bluetooth calls run while the gate is held; one that never completed (radio switched
    /// off or PC asleep mid-operation) would silently stop the program for good. A timeout
    /// turns that into an ordinary failure.
    /// </summary>
    private System.Runtime.CompilerServices.ConfiguredTaskAwaitable<bool> Bounded(Task<bool> call) =>
        call.WaitAsync(_operationTimeout).ConfigureAwait(false);

    private void SetState(KeeperState state)
    {
        if (_state == state) return;
        _state = state;
        StateChanged?.Invoke(state);
    }
}

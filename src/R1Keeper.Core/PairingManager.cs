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

    public PairingManager(IPultBluetooth bt, ISettingsStore store, RetryPolicy retry, Func<DateTimeOffset> now, Action<string> log)
    {
        _bt = bt;
        _store = store;
        _retry = retry;
        _now = now;
        _log = log;
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

    public async Task HandleAdvertisementAsync(PultAdvertisement adv)
    {
        if (_state == KeeperState.Paused) return;

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
                if (await _bt.IsPairedAsync(address).ConfigureAwait(false))
                {
                    // A pairing exists but the pult is advertising, so the pairing is stale.
                    // Remove it; pairing happens on the next advertisement.
                    SetState(KeeperState.Pairing);
                    if (await _bt.UnpairAsync(address).ConfigureAwait(false))
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
                if (await _bt.PairAsync(address).ConfigureAwait(false))
                {
                    _failures = 0;
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

    private void Fail(string what)
    {
        _failures++;
        var delay = _retry.DelayAfterFailure(_failures);
        _notBefore = _now() + delay;
        _log($"{what} (failure {_failures} in a row, next attempt in {delay.TotalSeconds:0} s)");
        SetState(KeeperState.Absent);
    }

    private void SetState(KeeperState state)
    {
        if (_state == state) return;
        _state = state;
        StateChanged?.Invoke(state);
    }
}

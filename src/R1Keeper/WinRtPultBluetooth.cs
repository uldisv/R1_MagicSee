using System.Diagnostics;
using R1Keeper.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;

namespace R1Keeper;

/// <summary>
/// The real Bluetooth layer, on the WinRT APIs.
///
/// Measured behaviour this code relies on (see the design document):
/// - a fresh <see cref="BluetoothLEDevice"/> object is taken for every pairing attempt;
/// - pairing is "Just Works": the program answers the ConfirmOnly prompt itself;
/// - a freshly opened object can briefly report Disconnected for a live link, so the
///   object held for link watching is preferred when asking about the link.
/// </summary>
public sealed class WinRtPultBluetooth(Action<string> log) : IPultBluetooth, IDisposable
{
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(10);

    private readonly object _sync = new();
    private BluetoothLEAdvertisementWatcher? _watcher;
    private System.Threading.Timer? _restartTimer;
    private bool _running;
    private string? _lastListenProblem;

    private BluetoothLEDevice? _watched;
    private ulong _watchedAddress;

    public event Action<PultAdvertisement>? AdvertisementReceived;
    public event Action<ulong>? LinkLost;

    /// <summary>Never throws. If listening cannot start or stops (radio off, PC sleep), it is retried every 10 s.</summary>
    public void Start()
    {
        lock (_sync) _running = true;
        TryStartWatcher();
    }

    public void Stop()
    {
        BluetoothLEAdvertisementWatcher? watcher;
        lock (_sync)
        {
            _running = false;
            watcher = _watcher;
            _watcher = null;
            _restartTimer?.Dispose();
            _restartTimer = null;
        }
        try { watcher?.Stop(); } catch (Exception) { /* already stopped */ }
    }

    private void TryStartWatcher()
    {
        lock (_sync)
        {
            if (!_running || _watcher is not null) return;
            try
            {
                var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
                watcher.Received += OnReceived;
                watcher.Stopped += OnStopped;
                watcher.Start();
                _watcher = watcher;
            }
            catch (Exception e)
            {
                ReportListenProblem("cannot listen for advertisements: " + e.Message);
                ScheduleRestart();
            }
        }
    }

    private void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs e)
    {
        if (_lastListenProblem is not null)
        {
            _lastListenProblem = null;
            log("listening for advertisements again");
        }
        AdvertisementReceived?.Invoke(new PultAdvertisement(e.BluetoothAddress, e.Advertisement.LocalName ?? ""));
    }

    private void OnStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs e)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(sender, _watcher)) return;
            _watcher = null;
            if (!_running) return;
            ReportListenProblem("listening for advertisements stopped: " + e.Error);
            ScheduleRestart();
        }
    }

    private void ReportListenProblem(string problem)
    {
        // The retry runs every 10 s; log a persisting problem only once.
        if (problem == _lastListenProblem) return;
        _lastListenProblem = problem;
        log(problem + "; retrying every 10 s");
    }

    private void ScheduleRestart()
    {
        _restartTimer?.Dispose();
        _restartTimer = new System.Threading.Timer(_ => TryStartWatcher(), null, RestartDelay, Timeout.InfiniteTimeSpan);
    }

    public async Task<bool> IsPairedAsync(ulong address)
    {
        using var device = await OpenAsync(address).ConfigureAwait(false);
        return device?.DeviceInformation.Pairing.IsPaired ?? false;
    }

    public async Task<bool> IsConnectedAsync(ulong address)
    {
        lock (_sync)
        {
            if (_watched is not null && _watchedAddress == address)
                return _watched.ConnectionStatus == BluetoothConnectionStatus.Connected;
        }
        using var device = await OpenAsync(address).ConfigureAwait(false);
        return device?.ConnectionStatus == BluetoothConnectionStatus.Connected;
    }

    public async Task<bool> UnpairAsync(ulong address)
    {
        var timer = Stopwatch.StartNew();
        ReleaseWatched(address);
        using var device = await OpenAsync(address).ConfigureAwait(false);
        if (device is null)
        {
            log("unpair: cannot open the device");
            return false;
        }
        var result = await device.DeviceInformation.Pairing.UnpairAsync();
        log($"unpair -> {result.Status} ({timer.Elapsed.TotalSeconds:F1} s)");
        return result.Status is DeviceUnpairingResultStatus.Unpaired or DeviceUnpairingResultStatus.AlreadyUnpaired;
    }

    public async Task<bool> PairAsync(ulong address)
    {
        var timer = Stopwatch.StartNew();
        ReleaseWatched(address);
        using var device = await OpenAsync(address).ConfigureAwait(false);
        if (device is null)
        {
            log("pair: cannot open the device");
            return false;
        }
        var custom = device.DeviceInformation.Pairing.Custom;
        custom.PairingRequested += (_, e) =>
        {
            if (e.PairingKind == DevicePairingKinds.ConfirmOnly) e.Accept();
        };
        var result = await custom.PairAsync(DevicePairingKinds.ConfirmOnly, DevicePairingProtectionLevel.Default);
        log($"pair -> {result.Status} ({timer.Elapsed.TotalSeconds:F1} s)");
        return result.Status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired;
    }

    public void WatchLink(ulong address) => _ = WatchLinkAsync(address);

    private async Task WatchLinkAsync(ulong address)
    {
        try
        {
            ReleaseWatched(address);
            var device = await OpenAsync(address).ConfigureAwait(false);
            if (device is null)
            {
                log("watch link: cannot open the device");
                return;
            }
            device.ConnectionStatusChanged += (sender, _) =>
            {
                if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected) LinkLost?.Invoke(address);
            };
            BluetoothLEDevice? previous;
            lock (_sync)
            {
                previous = _watched;
                _watched = device;
                _watchedAddress = address;
            }
            previous?.Dispose();
        }
        catch (Exception e)
        {
            log("watch link failed: " + e.Message);
        }
    }

    private void ReleaseWatched(ulong address)
    {
        BluetoothLEDevice? previous = null;
        lock (_sync)
        {
            if (_watched is not null && _watchedAddress == address)
            {
                previous = _watched;
                _watched = null;
            }
        }
        previous?.Dispose();
    }

    /// <summary>Opens a fresh device object; Windows can need a moment after an advertisement or an unpair.</summary>
    private static async Task<BluetoothLEDevice?> OpenAsync(ulong address)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
            if (device is not null) return device;
            await Task.Delay(250).ConfigureAwait(false);
        }
        return null;
    }

    public void Dispose()
    {
        Stop();
        BluetoothLEDevice? previous;
        lock (_sync)
        {
            previous = _watched;
            _watched = null;
        }
        previous?.Dispose();
    }
}

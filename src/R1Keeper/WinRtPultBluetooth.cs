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
    private static readonly TimeSpan HealthInterval = TimeSpan.FromSeconds(10);

    private readonly object _sync = new();
    private BluetoothLEAdvertisementWatcher? _watcher;
    private System.Threading.Timer? _healthTimer;
    private bool _running;
    private string? _lastListenProblem;

    private BluetoothLEDevice? _watched;
    private ulong _watchedAddress;

    public event Action<PultAdvertisement>? AdvertisementReceived;
    public event Action<ulong>? LinkLost;

    /// <summary>
    /// Never throws. Every 10 s a health check restarts listening if it is not running:
    /// the radio was off, the PC slept, or the watcher died without telling anyone.
    /// </summary>
    public void Start()
    {
        lock (_sync)
        {
            _running = true;
            _healthTimer ??= new System.Threading.Timer(_ => EnsureListening(), null, HealthInterval, HealthInterval);
        }
        EnsureListening();
    }

    public void Stop()
    {
        BluetoothLEAdvertisementWatcher? watcher;
        lock (_sync)
        {
            _running = false;
            watcher = _watcher;
            _watcher = null;
            _healthTimer?.Dispose();
            _healthTimer = null;
        }
        try { watcher?.Stop(); } catch (Exception) { /* already stopped */ }
    }

    private void EnsureListening()
    {
        lock (_sync)
        {
            if (!_running) return;
            if (_watcher is not null)
            {
                var status = _watcher.Status;
                if (status is BluetoothLEAdvertisementWatcherStatus.Created or BluetoothLEAdvertisementWatcherStatus.Started) return;
                ReportListenProblem("listening for advertisements is not running: " + status);
                _watcher = null;
            }

            // Stored before Start(): if Stopped is raised during Start, OnStopped must recognise it.
            var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
            watcher.Received += OnReceived;
            watcher.Stopped += OnStopped;
            _watcher = watcher;
            try
            {
                watcher.Start();
            }
            catch (Exception e)
            {
                _watcher = null;
                ReportListenProblem("cannot listen for advertisements: " + e.Message);
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
            _watcher = null; // the health check starts a new one
            if (_running) ReportListenProblem("listening for advertisements stopped: " + e.Error);
        }
    }

    private void ReportListenProblem(string problem)
    {
        // The health check runs every 10 s; log a persisting problem only once.
        if (problem == _lastListenProblem) return;
        _lastListenProblem = problem;
        log(problem + "; retrying every 10 s");
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
        var device = await OpenAsync(address).ConfigureAwait(false);
        if (device is null)
        {
            log("pair: cannot open the device");
            return false;
        }
        bool paired = false;
        try
        {
            var custom = device.DeviceInformation.Pairing.Custom;
            custom.PairingRequested += (_, e) =>
            {
                if (e.PairingKind == DevicePairingKinds.ConfirmOnly) e.Accept();
            };
            var result = await custom.PairAsync(DevicePairingKinds.ConfirmOnly, DevicePairingProtectionLevel.Default);
            log($"pair -> {result.Status} ({timer.Elapsed.TotalSeconds:F1} s)");
            paired = result.Status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired;

            // Keep this very object as the link watcher: its status is already current, so a
            // link-lost event that was queued before this pairing cannot be mistaken for a
            // dead link in the moment before a separate watcher object would have caught up.
            if (paired) Hold(device, address);
            return paired;
        }
        finally
        {
            if (!paired) device.Dispose();
        }
    }

    public void WatchLink(ulong address)
    {
        lock (_sync)
        {
            if (_watched is not null && _watchedAddress == address) return; // already watching (set by PairAsync)
        }
        _ = WatchLinkAsync(address);
    }

    private async Task WatchLinkAsync(ulong address)
    {
        try
        {
            var device = await OpenAsync(address).ConfigureAwait(false);
            if (device is null)
            {
                log("watch link: cannot open the device");
                return;
            }
            Hold(device, address);
            // The link may have dropped before the subscription existed.
            if (device.ConnectionStatus == BluetoothConnectionStatus.Disconnected) LinkLost?.Invoke(address);
        }
        catch (Exception e)
        {
            log("watch link failed: " + e.Message);
        }
    }

    private void Hold(BluetoothLEDevice device, ulong address)
    {
        device.ConnectionStatusChanged += OnConnectionStatusChanged;
        BluetoothLEDevice? previous;
        lock (_sync)
        {
            previous = _watched;
            _watched = device;
            _watchedAddress = address;
        }
        Release(previous);
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        ulong address;
        lock (_sync)
        {
            if (!ReferenceEquals(sender, _watched)) return; // an object we already let go of
            address = _watchedAddress;
        }
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected) LinkLost?.Invoke(address);
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
        Release(previous);
    }

    private void Release(BluetoothLEDevice? device)
    {
        if (device is null) return;
        try { device.ConnectionStatusChanged -= OnConnectionStatusChanged; } catch (Exception) { /* already gone */ }
        device.Dispose();
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
        Release(previous);
    }
}

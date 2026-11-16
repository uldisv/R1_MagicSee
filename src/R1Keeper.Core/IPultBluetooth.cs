namespace R1Keeper.Core;

/// <summary>One Bluetooth LE advertisement seen on the air.</summary>
public readonly record struct PultAdvertisement(ulong Address, string LocalName);

/// <summary>
/// Everything the pairing logic needs from Bluetooth. The logic never caches pairing or
/// link state; it always asks here, so changes made outside the program are respected.
/// </summary>
public interface IPultBluetooth
{
    event Action<PultAdvertisement>? AdvertisementReceived;

    /// <summary>The link to this address dropped.</summary>
    event Action<ulong>? LinkLost;

    /// <summary>Starts listening for advertisements.</summary>
    void Start();

    void Stop();

    Task<bool> IsPairedAsync(ulong address);

    Task<bool> IsConnectedAsync(ulong address);

    /// <returns>true when the device is no longer paired.</returns>
    Task<bool> UnpairAsync(ulong address);

    /// <returns>true when the device is paired.</returns>
    Task<bool> PairAsync(ulong address);

    /// <summary>(Re)subscribes <see cref="LinkLost"/> for this address.</summary>
    void WatchLink(ulong address);
}

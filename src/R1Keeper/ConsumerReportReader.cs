using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using R1Keeper.Core;

namespace R1Keeper;

/// <summary>
/// Reads input reports from the pult's media-key HID collection (usage page 0x0C, usage 1),
/// which is how the program learns that the pult is in music mode. Windows shares this
/// collection, unlike the keyboard and mouse ones, so plain file reads work.
/// </summary>
internal sealed class ConsumerReportReader(Action<string> log) : IDisposable
{
    private const ushort ConsumerUsagePage = 0x0C;
    private const ushort ConsumerControlUsage = 0x01;
    private const int FindAttempts = 15; // the HID nodes appear a few seconds after pairing

    private readonly object _sync = new();
    private CancellationTokenSource? _cts;

    public event Action<byte[]>? ReportReceived;

    /// <summary>In the background: looks for the collection once a second for up to 15 s, then reads until an error or Stop.</summary>
    public void Start(ulong address)
    {
        CancellationToken token;
        lock (_sync)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            token = _cts.Token;
        }
        _ = Task.Run(() => RunAsync(address, token));
    }

    public void Stop()
    {
        lock (_sync)
        {
            _cts?.Cancel();
            _cts = null;
        }
    }

    public void Dispose() => Stop();

    private async Task RunAsync(ulong address, CancellationToken token)
    {
        try
        {
            string needle = Settings.FormatAddress(address).ToLowerInvariant();
            SafeFileHandle? handle = null;
            int reportLength = 0;
            for (int attempt = 0; attempt < FindAttempts && handle is null; attempt++)
            {
                (handle, reportLength) = TryOpen(needle);
                if (handle is null) await Task.Delay(1000, token).ConfigureAwait(false);
            }
            if (handle is null)
            {
                log("mode reminder: the pult's media-key collection was not found");
                return;
            }

            using var stream = new FileStream(handle, FileAccess.Read, reportLength, isAsync: true);
            var buffer = new byte[reportLength];
            while (!token.IsCancellationRequested)
            {
                int read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
                if (read <= 0) break;
                ReportReceived?.Invoke(buffer[..read]);
            }
        }
        catch (OperationCanceledException)
        {
            // stopped
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            // the pult disconnected and the HID device went away
        }
        catch (Exception e)
        {
            log("mode reminder: reading failed: " + e.Message);
        }
    }

    private static (SafeFileHandle? handle, int reportLength) TryOpen(string addressNeedle)
    {
        foreach (var path in HidInterfacePaths())
        {
            if (!path.Contains(addressNeedle, StringComparison.OrdinalIgnoreCase)) continue;
            var handle = CreateFileW(path, GenericRead, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                handle.Dispose(); // keyboard and mouse collections are exclusive to Windows
                continue;
            }
            if (ReadCaps(handle, out ushort usagePage, out ushort usage, out ushort inputLength) &&
                usagePage == ConsumerUsagePage && usage == ConsumerControlUsage && inputLength > 0)
            {
                return (handle, inputLength);
            }
            handle.Dispose();
        }
        return (null, 0);
    }

    private static bool ReadCaps(SafeFileHandle handle, out ushort usagePage, out ushort usage, out ushort inputLength)
    {
        usagePage = usage = inputLength = 0;
        if (!HidD_GetPreparsedData(handle, out IntPtr preparsed)) return false;
        try
        {
            // HIDP_CAPS: Usage (0), UsagePage (2), InputReportByteLength (4), ... 64 bytes in all
            var caps = new byte[64];
            if (HidP_GetCaps(preparsed, caps) != HidpStatusSuccess) return false;
            usage = BitConverter.ToUInt16(caps, 0);
            usagePage = BitConverter.ToUInt16(caps, 2);
            inputLength = BitConverter.ToUInt16(caps, 4);
            return true;
        }
        finally
        {
            HidD_FreePreparsedData(preparsed);
        }
    }

    private static IEnumerable<string> HidInterfacePaths()
    {
        HidD_GetHidGuid(out Guid hidGuid);
        if (CM_Get_Device_Interface_List_SizeW(out int length, ref hidGuid, null, 0) != 0 || length <= 1) return [];
        var buffer = new char[length];
        if (CM_Get_Device_Interface_ListW(ref hidGuid, null, buffer, length, 0) != 0) return [];
        return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 1;
    private const uint FileShareWrite = 2;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;
    private const int HidpStatusSuccess = 0x00110000;

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr preparsedData, [Out] byte[] capabilities);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_List_SizeW(out int length, ref Guid interfaceClass, string? deviceId, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_ListW(ref Guid interfaceClass, string? deviceId, [Out] char[] buffer, int length, int flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
}

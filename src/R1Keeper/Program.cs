using R1Keeper.Core;

namespace R1Keeper;

internal static class AppPaths
{
    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "R1Keeper");

    public static string LogFile => Path.Combine(DataDir, "r1keeper.log");

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
}

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var log = new FileLog(AppPaths.LogFile);

        if (args.Contains("--probe"))
        {
            Task.Run(() => ProbeAsync(log)).GetAwaiter().GetResult();
            return;
        }
    }

    /// <summary>
    /// Diagnostic mode: logs what Windows knows about the pult and which matching
    /// advertisements are on the air for 15 s. Changes nothing.
    /// </summary>
    private static async Task ProbeAsync(FileLog log)
    {
        var settings = new JsonSettingsStore(AppPaths.SettingsFile).Load();
        using var bt = new WinRtPultBluetooth(log.Write);
        try
        {
            if (Settings.ParseAddress(settings.PultAddress) is { } address)
            {
                bool paired = await bt.IsPairedAsync(address);
                bool connected = await bt.IsConnectedAsync(address);
                log.Write($"probe: address={Settings.FormatAddress(address)} paired={paired} connected={connected}");
            }
            else
            {
                log.Write("probe: address=none");
            }

            bt.AdvertisementReceived += adv =>
            {
                if (adv.LocalName.StartsWith(settings.NamePrefix, StringComparison.Ordinal))
                    log.Write($"probe: adv {Settings.FormatAddress(adv.Address)} '{adv.LocalName}'");
            };
            bt.Start();
            await Task.Delay(TimeSpan.FromSeconds(15));
            bt.Stop();
            log.Write("probe: done");
        }
        catch (Exception e)
        {
            log.Write("probe: failed: " + e);
        }
    }
}

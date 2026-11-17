using R1Keeper.Core;

namespace R1Keeper;

internal static class AppPaths
{
    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "R1Keeper");

    public static string LogFile => Path.Combine(DataDir, "r1keeper.log");

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");

    // Both names are also used by installer\R1Keeper.iss; keep them in step.
    public const string SingleInstanceMutex = @"Local\R1Keeper.SingleInstance";

    /// <summary>Signalling this event makes the running copy exit as if Exit had been clicked.</summary>
    public const string ExitEvent = @"Local\R1Keeper.Exit";
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

        // One instance per user session; a second start ends quietly.
        using var single = new Mutex(initiallyOwned: true, AppPaths.SingleInstanceMutex, out bool isFirst);
        if (!isFirst) return;
        using var exitRequest = new EventWaitHandle(false, EventResetMode.AutoReset, AppPaths.ExitEvent);

        // Nothing here may ever show a window: log unexpected errors instead.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => log.Write("unhandled UI error: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Write("unhandled error: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            log.Write("unobserved task error: " + e.Exception);
            e.SetObserved();
        };

        ApplicationConfiguration.Initialize();
        try { Autostart.RepairPath(); }
        catch (Exception e) { log.Write("repairing autostart failed: " + e.Message); }

        log.Write("R1 Keeper started");
        using var bluetooth = new WinRtPultBluetooth(log.Write);
        using var reader = new ConsumerReportReader(log.Write);
        var manager = new PairingManager(bluetooth, new JsonSettingsStore(AppPaths.SettingsFile), new RetryPolicy(), () => DateTimeOffset.UtcNow, log.Write);
        manager.StateChanged += state => log.Write("state: " + state);
        using var tray = new TrayApp(manager, bluetooth, reader, new ModeReminder(), log.Write);

        // The installer and uninstaller signal this to close the program before replacing its files.
        var exitWait = ThreadPool.RegisterWaitForSingleObject(exitRequest, (_, _) =>
        {
            log.Write("exit requested by another program");
            tray.RequestExit();
        }, null, Timeout.Infinite, executeOnlyOnce: true);

        _ = Task.Run(async () =>
        {
            try { await manager.StartAsync(); }
            catch (Exception e) { log.Write("start failed: " + e); }
        });

        Application.Run(tray);
        exitWait.Unregister(null);
        log.Write("R1 Keeper exited");
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

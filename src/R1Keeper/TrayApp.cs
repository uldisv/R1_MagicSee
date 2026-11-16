using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using R1Keeper.Core;

namespace R1Keeper;

/// <summary>
/// The whole user interface: a notification-area icon with a menu and balloons.
/// There is deliberately no window and nothing here ever takes focus.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private const string Title = "R1 Keeper";
    private const string MusicModeText = "Pults ir mūzikas režīmā. Nospied M + B, lai pārslēgtu uz spēļu režīmu.";

    private readonly PairingManager _manager;
    private readonly WinRtPultBluetooth _bluetooth;
    private readonly ConsumerReportReader _reader;
    private readonly ModeReminder _reminder;
    private readonly Action<string> _log;

    private readonly Control _ui = new();   // only used to get back onto the UI thread
    private readonly NotifyIcon _icon = new();
    private readonly Dictionary<KeeperState, Icon> _icons = new();
    private readonly ToolStripMenuItem _statusItem = new() { Enabled = false };
    private readonly ToolStripMenuItem _pauseItem = new();
    private readonly ToolStripMenuItem _forgetItem = new("Aizmirst pulti");
    private readonly ToolStripMenuItem _autostartItem = new("Startēt kopā ar Windows");

    public TrayApp(PairingManager manager, WinRtPultBluetooth bluetooth, ConsumerReportReader reader, ModeReminder reminder, Action<string> log)
    {
        _manager = manager;
        _bluetooth = bluetooth;
        _reader = reader;
        _reminder = reminder;
        _log = log;

        _ui.CreateControl();
        _ = _ui.Handle;

        _icons[KeeperState.Absent] = DrawIcon(Color.Gray, paused: false);
        _icons[KeeperState.Pairing] = DrawIcon(Color.Orange, paused: false);
        _icons[KeeperState.Connected] = DrawIcon(Color.LimeGreen, paused: false);
        _icons[KeeperState.Paused] = DrawIcon(Color.Gray, paused: true);

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_forgetItem);
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripMenuItem("Atvērt žurnālu", null, (_, _) => OpenLog()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Iziet", null, (_, _) => Exit()));
        menu.Opening += (_, _) => RefreshMenu();

        _pauseItem.Click += (_, _) => Run(_manager.State == KeeperState.Paused ? _manager.ResumeAsync : _manager.PauseAsync);
        _forgetItem.Click += (_, _) => Run(_manager.ForgetPultAsync);
        _autostartItem.Click += (_, _) => ToggleAutostart();

        _icon.ContextMenuStrip = menu;
        _icon.Visible = true;

        _manager.StateChanged += state => OnUi(() => ShowState(state));
        _manager.PultLearned += address => OnUi(() => Balloon("Pults iegaumēta: " + Settings.DisplayAddress(address)));
        _reminder.MusicModeDetected += () => OnUi(() => Balloon(MusicModeText));
        _reader.ReportReceived += report => _reminder.OnConsumerReport(report);

        ShowState(_manager.State);
    }

    private static string StateText(KeeperState state) => state switch
    {
        KeeperState.Pairing => "R1 Keeper — notiek pārošana",
        KeeperState.Connected => "R1 Keeper — pults pievienota",
        KeeperState.Paused => "R1 Keeper — pauzēts",
        _ => "R1 Keeper — pults nav redzama",
    };

    private void ShowState(KeeperState state)
    {
        _icon.Icon = _icons[state];
        _icon.Text = StateText(state);
        _statusItem.Text = StateText(state);

        // The mode reminder only makes sense while the pult is connected.
        if (state == KeeperState.Connected && _manager.PultAddress is { } address)
        {
            _reminder.OnConnected();
            _reader.Start(address);
        }
        else
        {
            _reader.Stop();
        }
    }

    private void RefreshMenu()
    {
        _statusItem.Text = StateText(_manager.State);
        _pauseItem.Text = _manager.State == KeeperState.Paused ? "Turpināt" : "Pauzēt";
        _forgetItem.Enabled = _manager.PultAddress is not null;
        _autostartItem.Checked = SafeAutostartEnabled();
    }

    private bool SafeAutostartEnabled()
    {
        try { return Autostart.IsEnabled(); }
        catch (Exception e) { _log("reading autostart failed: " + e.Message); return false; }
    }

    private void ToggleAutostart()
    {
        try { Autostart.Set(!Autostart.IsEnabled()); }
        catch (Exception e) { _log("changing autostart failed: " + e.Message); }
    }

    private void OpenLog()
    {
        try
        {
            _log("log opened from the menu");
            Process.Start(new ProcessStartInfo(AppPaths.LogFile) { UseShellExecute = true });
        }
        catch (Exception e) { _log("opening the log failed: " + e.Message); }
    }

    private void Balloon(string text) => _icon.ShowBalloonTip(5000, Title, text, ToolTipIcon.Info);

    private async void Run(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) { _log("menu action failed: " + e); }
    }

    private void OnUi(Action action)
    {
        try { _ui.BeginInvoke(action); }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException) { /* exiting */ }
    }

    /// <summary>Leaves the pairing alone: the pult keeps working until its next disconnect.</summary>
    private void Exit()
    {
        _reader.Dispose();
        _bluetooth.Dispose();
        _icon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icon.Dispose();
            _ui.Dispose();
            foreach (var icon in _icons.Values)
            {
                DestroyIcon(icon.Handle);
                icon.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    private static Icon DrawIcon(Color color, bool paused)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var fill = new SolidBrush(color);
            g.FillEllipse(fill, 2, 2, 28, 28);
            using var outline = new Pen(Color.FromArgb(90, 0, 0, 0), 2);
            g.DrawEllipse(outline, 2, 2, 28, 28);
            if (paused)
            {
                g.FillRectangle(Brushes.White, 10, 9, 4, 14);
                g.FillRectangle(Brushes.White, 18, 9, 4, 14);
            }
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}

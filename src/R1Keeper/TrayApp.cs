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
    private readonly ToolStripMenuItem _forgetItem = new();
    private readonly ToolStripMenuItem _autostartItem = new();
    private readonly ToolStripMenuItem _languageItem = new();
    private readonly ToolStripMenuItem _openLogItem = new();
    private readonly ToolStripMenuItem _exitItem = new();

    private UiLanguage _language;
    private UiText _text;
    private KeeperState _shownState;   // the state the icon shows
    private bool _exiting;

    public TrayApp(PairingManager manager, WinRtPultBluetooth bluetooth, ConsumerReportReader reader, ModeReminder reminder, Action<string> log)
    {
        _manager = manager;
        _bluetooth = bluetooth;
        _reader = reader;
        _reminder = reminder;
        _log = log;

        _language = SafeLoadLanguage();
        _text = UiText.For(_language);

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
        menu.Items.Add(_languageItem);
        menu.Items.Add(_openLogItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_exitItem);
        menu.Opening += (_, _) => RefreshMenu();

        foreach (var language in UiLanguages.All)
        {
            // Each language is named in itself, so the list reads the same whatever the menu language.
            var item = new ToolStripMenuItem(UiLanguages.NativeName(language)) { Tag = language };
            item.Click += (_, _) => ChangeLanguage(language);
            _languageItem.DropDownItems.Add(item);
        }

        _pauseItem.Click += (_, _) => Run(_manager.State == KeeperState.Paused ? _manager.ResumeAsync : _manager.PauseAsync);
        _forgetItem.Click += (_, _) => Run(_manager.ForgetPultAsync);
        _autostartItem.Click += (_, _) => ToggleAutostart();
        _openLogItem.Click += (_, _) => OpenLog();
        _exitItem.Click += (_, _) => Exit();

        ApplyTexts();
        _icon.ContextMenuStrip = menu;
        _icon.Visible = true;

        _manager.StateChanged += state => OnUi(() => ShowState(state));
        _manager.PultLearned += address => OnUi(() => Balloon(_text.PultLearned(Settings.DisplayAddress(address))));
        _reminder.MusicModeDetected += () => OnUi(() => Balloon(_text.MusicMode));
        _reader.ReportReceived += report => _reminder.OnConsumerReport(report);

        ShowState(_manager.State);
    }

    /// <summary>Closes the program from another thread (used when the installer asks it to exit).</summary>
    public void RequestExit() => OnUi(Exit);

    private void ApplyTexts()
    {
        _forgetItem.Text = _text.Forget;
        _autostartItem.Text = _text.Autostart;
        _languageItem.Text = _text.Language;
        _openLogItem.Text = _text.OpenLog;
        _exitItem.Text = _text.Exit;
        RefreshMenu();
        _icon.Text = _text.State(_shownState);
    }

    private void ShowState(KeeperState state)
    {
        _shownState = state;
        _icon.Icon = _icons[state];
        _icon.Text = _text.State(state);
        _statusItem.Text = _text.State(state);

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
        _statusItem.Text = _text.State(_manager.State);
        _pauseItem.Text = _manager.State == KeeperState.Paused ? _text.Resume : _text.Pause;
        _forgetItem.Enabled = _manager.PultAddress is not null;
        _autostartItem.Checked = SafeAutostartEnabled();
        foreach (ToolStripMenuItem item in _languageItem.DropDownItems)
            item.Checked = (UiLanguage)item.Tag! == _language;
    }

    private UiLanguage SafeLoadLanguage()
    {
        try { return LanguagePreference.Load(); }
        catch (Exception e) { _log("reading the language failed: " + e.Message); return UiLanguages.Default; }
    }

    private void ChangeLanguage(UiLanguage language)
    {
        if (language == _language) return;
        // Switch even if saving fails, so the choice holds at least until the program exits.
        try { LanguagePreference.Save(language); }
        catch (Exception e) { _log("saving the language failed: " + e.Message); }
        _log("language: " + UiLanguages.Code(language));
        _language = language;
        _text = UiText.For(language);
        ApplyTexts();
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
        if (_exiting) return; // the menu and the installer can both ask
        _exiting = true;
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

namespace R1Keeper.Core;

/// <summary>
/// Notices that the pult is in music mode. After power-on the R1 sends its stick and A–D
/// buttons as media keys (volume, next track, play/pause); a media-key report with any key
/// down therefore means "not in game mode". Fires at most once per connection.
/// </summary>
public sealed class ModeReminder
{
    private int _armed; // 1 = one reminder may still be given for the current connection

    public event Action? MusicModeDetected;

    /// <summary>Allows one new reminder.</summary>
    public void OnConnected() => Interlocked.Exchange(ref _armed, 1);

    /// <param name="report">report[0] is the report ID; the remaining bytes are key bits.</param>
    public void OnConsumerReport(ReadOnlySpan<byte> report)
    {
        if (report.Length < 2 || report[1..].IndexOfAnyExcept((byte)0) < 0) return;
        if (Interlocked.Exchange(ref _armed, 0) == 1) MusicModeDetected?.Invoke();
    }
}

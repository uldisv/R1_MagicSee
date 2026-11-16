using System.Globalization;

namespace R1Keeper.Core;

/// <summary>
/// Append-only text log with a size cap: when the file reaches <c>maxBytes</c> it is renamed
/// to "&lt;file&gt;.1" (replacing the previous one) and a new file is started.
/// Thread-safe. Never throws: logging must not be able to break the program.
/// </summary>
public sealed class FileLog(string filePath, long maxBytes = 1_000_000)
{
    private readonly object _gate = new();

    public void Write(string message)
    {
        var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + message + Environment.NewLine;
        lock (_gate)
        {
            try
            {
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var info = new FileInfo(filePath);
                if (info.Exists && info.Length >= maxBytes) File.Move(filePath, filePath + ".1", overwrite: true);
                File.AppendAllText(filePath, line);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                // nowhere to report a logging failure
            }
        }
    }
}

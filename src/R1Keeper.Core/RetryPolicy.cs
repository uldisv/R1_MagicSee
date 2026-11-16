namespace R1Keeper.Core;

/// <summary>
/// How long to wait before the next pairing attempt after N consecutive failures.
/// The first three retries are free, because one failed attempt is expected when the
/// pult was switched off and on quickly; after that the wait grows up to 30 s.
/// </summary>
public sealed class RetryPolicy
{
    private static readonly int[] DelaysSeconds = [5, 10, 20, 30];
    private const int FreeRetries = 3;

    public TimeSpan DelayAfterFailure(int consecutiveFailures)
    {
        if (consecutiveFailures <= FreeRetries) return TimeSpan.Zero;
        int index = Math.Min(consecutiveFailures - FreeRetries - 1, DelaysSeconds.Length - 1);
        return TimeSpan.FromSeconds(DelaysSeconds[index]);
    }
}

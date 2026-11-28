using System.Collections.Concurrent;

using Sheaf.Core.Authentication;

namespace Sheaf.Email;

/// <summary>
/// Limits how often one address gets mail it didn't necessarily ask for, so the anonymous endpoints can't be used to
/// flood someone's inbox. In memory: the API runs as a single replica. Codes also have a daily cap per account and
/// purpose, stored with the code (<see cref="EmailCodeTokenProvider"/>), which survives restarts.
/// </summary>
internal sealed class EmailCooldown(TimeProvider time)
{
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
    private const int PruneThreshold = 10_000;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSent = new(StringComparer.Ordinal);

    /// <summary>Records a send for <paramref name="key"/> and returns true, unless one was recorded less than a minute ago.</summary>
    public bool TryStart(string key)
    {
        DateTimeOffset now = time.GetUtcNow();
        if (_lastSent.Count > PruneThreshold)
        {
            foreach (KeyValuePair<string, DateTimeOffset> entry in _lastSent)
            {
                // Removes the entry only if it still holds the time read here, so a send recorded meanwhile stays.
                if (now - entry.Value >= Interval)
                {
                    _lastSent.TryRemove(entry);
                }
            }
        }

        while (true)
        {
            if (_lastSent.TryGetValue(key, out DateTimeOffset last))
            {
                if (now - last < Interval)
                {
                    return false;
                }
                if (_lastSent.TryUpdate(key, now, last))
                {
                    return true;
                }
            }
            else if (_lastSent.TryAdd(key, now))
            {
                return true;
            }
        }
    }
}

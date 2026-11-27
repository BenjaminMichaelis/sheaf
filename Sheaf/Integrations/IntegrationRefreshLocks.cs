using System.Collections.Concurrent;

namespace Sheaf.Integrations;

/// <summary>One lock per integration, so credential refreshes within this process happen one at a time.</summary>
public sealed class IntegrationRefreshLocks
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public SemaphoreSlim For(Guid integrationId) => _locks.GetOrAdd(integrationId, _ => new SemaphoreSlim(1, 1));
}

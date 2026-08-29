using Microsoft.Extensions.Caching.Memory;

namespace RemoteShutdown.Agent.Core.Security;

/// <summary>
/// Tracks recently-seen (clientId, nonce) pairs so a captured-and-replayed request
/// is rejected even if it arrives within the timestamp validity window.
/// </summary>
public sealed class NonceCache
{
    private readonly IMemoryCache _cache;

    // Must be >= the timestamp validity window (docs/protocol.md §5: 30s) with margin
    // for clock drift between phone and PC.
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    public NonceCache(IMemoryCache cache) => _cache = cache;

    /// <summary>Returns true and records the nonce if it hasn't been seen before; false if it's a replay.</summary>
    public bool TryRegister(string clientId, string nonce)
    {
        var key = $"nonce:{clientId}:{nonce}";
        if (_cache.TryGetValue(key, out _))
            return false;

        _cache.Set(key, true, Window);
        return true;
    }
}

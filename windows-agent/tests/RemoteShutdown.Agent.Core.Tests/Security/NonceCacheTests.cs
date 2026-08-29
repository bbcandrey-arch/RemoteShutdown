using Microsoft.Extensions.Caching.Memory;
using RemoteShutdown.Agent.Core.Security;

namespace RemoteShutdown.Agent.Core.Tests.Security;

public class NonceCacheTests
{
    private static NonceCache CreateCache() => new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public void First_use_of_a_nonce_is_accepted()
    {
        var cache = CreateCache();
        Assert.True(cache.TryRegister("client-1", "nonce-1"));
    }

    [Fact]
    public void Replaying_the_same_nonce_for_the_same_client_is_rejected()
    {
        var cache = CreateCache();
        cache.TryRegister("client-1", "nonce-1");

        Assert.False(cache.TryRegister("client-1", "nonce-1"));
    }

    [Fact]
    public void The_same_nonce_value_is_independent_per_client()
    {
        var cache = CreateCache();
        cache.TryRegister("client-1", "nonce-1");

        Assert.True(cache.TryRegister("client-2", "nonce-1"));
    }
}

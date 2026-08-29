using RemoteShutdown.Agent.Core.Security;

namespace RemoteShutdown.Agent.Core.Tests.Security;

public class PinHasherTests
{
    [Fact]
    public void Verify_accepts_the_correct_pin()
    {
        var hash = PinHasher.Hash("123456");
        Assert.True(PinHasher.Verify("123456", hash));
    }

    [Fact]
    public void Verify_rejects_the_wrong_pin()
    {
        var hash = PinHasher.Hash("123456");
        Assert.False(PinHasher.Verify("000000", hash));
    }

    [Fact]
    public void Hash_is_salted_so_the_same_pin_hashes_differently_each_time()
    {
        Assert.NotEqual(PinHasher.Hash("123456"), PinHasher.Hash("123456"));
    }
}

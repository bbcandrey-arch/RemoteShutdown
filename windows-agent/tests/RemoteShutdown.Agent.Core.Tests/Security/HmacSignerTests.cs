using RemoteShutdown.Agent.Core.Security;

namespace RemoteShutdown.Agent.Core.Tests.Security;

public class HmacSignerTests
{
    private static readonly byte[] Secret = HmacSigner.GenerateSharedSecret();

    [Fact]
    public void Verify_accepts_a_signature_produced_by_Sign()
    {
        var sig = HmacSigner.Sign(Secret, "POST", "/commands/shutdown", 1_700_000_000_000, "nonce-1", "{\"delaySeconds\":0}");

        Assert.True(HmacSigner.Verify(Secret, "POST", "/commands/shutdown", 1_700_000_000_000, "nonce-1", "{\"delaySeconds\":0}", sig));
    }

    [Fact]
    public void Verify_rejects_a_tampered_body()
    {
        var sig = HmacSigner.Sign(Secret, "POST", "/commands/shutdown", 1_700_000_000_000, "nonce-1", "{\"delaySeconds\":0}");

        Assert.False(HmacSigner.Verify(Secret, "POST", "/commands/shutdown", 1_700_000_000_000, "nonce-1", "{\"delaySeconds\":9999}", sig));
    }

    [Fact]
    public void Verify_rejects_wrong_secret()
    {
        var sig = HmacSigner.Sign(Secret, "POST", "/commands/shutdown", 1_700_000_000_000, "nonce-1", "{}");
        var otherSecret = HmacSigner.GenerateSharedSecret();

        Assert.False(HmacSigner.Verify(otherSecret, "POST", "/commands/shutdown", 1_700_000_000_000, "nonce-1", "{}", sig));
    }

    [Fact]
    public void Verify_rejects_malformed_signature_without_throwing()
    {
        Assert.False(HmacSigner.Verify(Secret, "POST", "/x", 1, "n", "{}", "not-base64!!"));
    }
}

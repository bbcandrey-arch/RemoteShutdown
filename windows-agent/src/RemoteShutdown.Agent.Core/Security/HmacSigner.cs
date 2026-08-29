using System.Security.Cryptography;
using System.Text;

namespace RemoteShutdown.Agent.Core.Security;

/// <summary>
/// Computes/verifies the request signature described in docs/protocol.md §5:
/// HMAC_SHA256(sharedSecret, method + "\n" + path + "\n" + timestamp + "\n" + nonce + "\n" + body)
/// </summary>
public static class HmacSigner
{
    public static string Sign(byte[] sharedSecret, string method, string path, long timestampMs, string nonce, string body)
    {
        var message = $"{method}\n{path}\n{timestampMs}\n{nonce}\n{body}";
        var bytes = Encoding.UTF8.GetBytes(message);
        var signature = HMACSHA256.HashData(sharedSecret, bytes);
        return Convert.ToBase64String(signature);
    }

    public static bool Verify(byte[] sharedSecret, string method, string path, long timestampMs, string nonce, string body, string signatureBase64)
    {
        string expected;
        try
        {
            expected = Sign(sharedSecret, method, path, timestampMs, nonce, body);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] expectedBytes;
        byte[] actualBytes;
        try
        {
            expectedBytes = Convert.FromBase64String(expected);
            actualBytes = Convert.FromBase64String(signatureBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    public static byte[] GenerateSharedSecret() => RandomNumberGenerator.GetBytes(32);
}

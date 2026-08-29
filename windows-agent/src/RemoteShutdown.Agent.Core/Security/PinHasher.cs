using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace RemoteShutdown.Agent.Core.Security;

/// <summary>
/// PBKDF2 hashing/verification for the pairing PIN. The PIN itself never touches
/// disk or the SQLite DB — only this salted hash does.
/// </summary>
public static class PinHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 210_000;

    /// <summary>Format: base64(salt) + "." + base64(hash) + "." + iterations</summary>
    public static string Hash(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = KeyDerivation.Pbkdf2(pin, salt, KeyDerivationPrf.HMACSHA256, Iterations, HashSize);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}.{Iterations}";
    }

    public static bool Verify(string pin, string encoded)
    {
        var parts = encoded.Split('.');
        if (parts.Length != 3) return false;

        var salt = Convert.FromBase64String(parts[0]);
        var expected = Convert.FromBase64String(parts[1]);
        var iterations = int.Parse(parts[2]);

        var actual = KeyDerivation.Pbkdf2(pin, salt, KeyDerivationPrf.HMACSHA256, iterations, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

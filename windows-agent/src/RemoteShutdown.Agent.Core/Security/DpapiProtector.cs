using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace RemoteShutdown.Agent.Core.Security;

/// <summary>
/// Encrypts secrets (shared HMAC keys) at rest using Windows DPAPI, machine scope,
/// so the SQLite file alone is useless if copied to another machine.
/// </summary>
[SupportedOSPlatform("windows")]
public static class DpapiProtector
{
    // Binds decryption to this specific agent purpose so DPAPI-protected blobs
    // from an unrelated app on the same machine can't be swapped in.
    private static readonly byte[] SharedSecretEntropy = "RemoteShutdown.Agent.SharedSecret.v1"u8.ToArray();

    // Отдельная "цель" для PIN в открытом виде (хранится только для показа в UI трея —
    // см. SettingsForm), чтобы её нельзя было скормить в Unprotect как чужой секрет
    // устройства и наоборот.
    private static readonly byte[] PinEntropy = "RemoteShutdown.Agent.PinPlaintext.v1"u8.ToArray();

    public static byte[] Protect(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, SharedSecretEntropy, DataProtectionScope.LocalMachine);

    public static byte[] Unprotect(byte[] ciphertext) =>
        ProtectedData.Unprotect(ciphertext, SharedSecretEntropy, DataProtectionScope.LocalMachine);

    public static byte[] ProtectPin(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, PinEntropy, DataProtectionScope.LocalMachine);

    public static byte[] UnprotectPin(byte[] ciphertext) =>
        ProtectedData.Unprotect(ciphertext, PinEntropy, DataProtectionScope.LocalMachine);
}

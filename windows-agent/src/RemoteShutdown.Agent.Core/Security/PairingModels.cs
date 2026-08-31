namespace RemoteShutdown.Agent.Core.Security;

public sealed record PairedDevice(
    string ClientId,
    string DeviceName,
    byte[] SharedSecret,
    DateTime PairedAtUtc,
    DateTime? LastSeenUtc,
    bool Revoked,
    // Платформа ("Android"/"iOS"/...) и модель устройства ("SM-S908E", "Pixel 7", ...),
    // если клиент их прислал при пейринге — показываются на вкладке "Устройства" в трее,
    // чтобы отличать сопряжённые телефоны друг от друга не только по имени. Null для
    // старых клиентов/записей, до появления этого поля. См. docs/roadmap.md.
    string? Platform = null,
    string? Model = null,
    // Версия мобильного приложения ("1.2.0"), если клиент её прислал — показывается в
    // трее вместо дублирующей "Модель" (которая часто совпадала с "Устройство"), заодно
    // полезно понимать, кто из сопряжённых телефонов ещё не обновился. См. docs/roadmap.md.
    string? AppVersion = null);

public sealed record PairingSession(
    string PairingSessionId,
    string DeviceName,
    DateTime CreatedAtUtc,
    string? Platform = null,
    string? Model = null,
    string? AppVersion = null);

public enum PairConfirmResult
{
    Success,
    InvalidPin,
    Locked,
    SessionNotFound,
    SessionExpired,
}

public sealed record PairConfirmOutcome(
    PairConfirmResult Result,
    PairedDevice? Device = null,
    TimeSpan? LockRemaining = null);

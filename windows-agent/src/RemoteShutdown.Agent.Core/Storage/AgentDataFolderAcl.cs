using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace RemoteShutdown.Agent.Core.Storage;

/// <summary>
/// Служба (AgentHost.CreateApp) работает от LocalSystem, а Tray — от обычного
/// залогиненного пользователя, но оба пишут в одну и ту же папку
/// %ProgramData%\RemoteShutdownAgent (agent.db — см. AgentDatabase, pairing-qr.png —
/// см. PairingQrService). Эта папка создаётся инсталлятором внутри {commonappdata}, а
/// у C:\ProgramData по умолчанию обычные пользователи имеют только "Чтение и
/// выполнение", без записи. Пока и инсталлятор, и служба, и трей запускались на
/// машине разработки под одной и той же (администраторской) учёткой без UAC-урезания
/// токена, это было незаметно — но на машине реального пользователя, где Tray
/// работает от обычного залогиненного профиля, отличного от того, что ставил агент,
/// это валит SettingsForm сразу при открытии: UnauthorizedAccessException на
/// 'pairing-qr.png' (PairingQrService.GenerateAndSave → File.WriteAllBytes).
///
/// Вызывается только из Service (см. AgentHost.CreateApp) — единственный процесс с
/// достаточными правами поменять ACL чужой папки — на каждом старте, идемпотентно,
/// чтобы самовосстанавливаться даже без переустановки: достаточно перезапустить
/// службу или перезагрузить ПК, полная переустановка не нужна.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AgentDataFolderAcl
{
    public static void EnsureWritableByInteractiveUsers(string directory)
    {
        try
        {
            var info = new DirectoryInfo(directory);
            var security = info.GetAccessControl(AccessControlSections.Access);

            var authenticatedUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            security.AddAccessRule(new FileSystemAccessRule(
                authenticatedUsers,
                FileSystemRights.Modify,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            info.SetAccessControl(security);
        }
        catch
        {
            // Не критично для запуска агента: в худшем случае Tray снова упрётся в ту
            // же Access Denied, что и до этого фикса (например, если ACL этой папки
            // дополнительно урезаны групповой политикой) — но сам Service продолжит
            // работать, а не упадёт из-за этого при старте.
        }
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using RemoteShutdown.Agent.Core.Storage;

namespace RemoteShutdown.Agent.Core.Power;

public enum PowerAction { Shutdown, Restart, Sleep, Hibernate, Lock }

public sealed class HibernateNotSupportedException : Exception
{
    public HibernateNotSupportedException()
        : base("Hibernate is disabled on this machine (run 'powercfg /hibernate on').") { }
}

/// <summary>
/// Executes power actions requested via docs/protocol.md §6.
/// Shutdown/Restart shell out to shutdown.exe (works from a non-interactive service
/// session, no elevation dance needed). Sleep/Hibernate/Lock use Win32 APIs and
/// require running in the interactive user session — see windows-agent/src/.../Tray
/// for why that split exists (Session 0 isolation).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PowerActionsService
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    private readonly SettingsStore? _settingsStore;

    /// <summary>
    /// Test-only/no-store fallback constructor (used by unit tests) — behaves as if
    /// test mode is permanently on, matching the historical hardcoded-true default.
    /// </summary>
    public PowerActionsService() { }

    /// <summary>
    /// Constructor used by the running agent: test mode is now a persisted setting
    /// (SettingsStore.Keys.TestMode) toggled from the tray Settings UI, so it can be
    /// switched on/off without a rebuild or an agent restart — see docs/security.md,
    /// "Заглушка опасных действий".
    /// </summary>
    public PowerActionsService(SettingsStore settingsStore) => _settingsStore = settingsStore;

    /// <summary>
    /// Dev/test safety switch: when true, every action this service would normally
    /// perform instead just launches Calculator, so manual smoke-testing (curl/
    /// PowerShell against a real machine, or a live timer firing) can't actually shut
    /// down / restart / sleep / lock the machine. A real shutdown happened this way
    /// once already — see project history. Default is true (safe) whenever the
    /// setting has never been explicitly set.
    /// </summary>
    public bool SimulateDangerousActions
    {
        get => _settingsStore is null
            ? _simulateDangerousActionsFallback
            : _settingsStore.Get(SettingsStore.Keys.TestMode) != "false";
        set
        {
            if (_settingsStore is null) _simulateDangerousActionsFallback = value;
            else _settingsStore.Set(SettingsStore.Keys.TestMode, value ? "true" : "false");
        }
    }

    private bool _simulateDangerousActionsFallback = true;

    public void Execute(PowerAction action, int delaySeconds = 0)
    {
        if (SimulateDangerousActions)
        {
            RunStub(delaySeconds);
            return;
        }

        switch (action)
        {
            case PowerAction.Shutdown:
                RunShutdownExe($"/s /t {delaySeconds}");
                break;
            case PowerAction.Restart:
                RunShutdownExe($"/r /t {delaySeconds}");
                break;
            case PowerAction.Sleep:
                if (!SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false))
                    throw new InvalidOperationException($"SetSuspendState(sleep) failed: Win32 error {Marshal.GetLastWin32Error()}");
                break;
            case PowerAction.Hibernate:
                if (!IsHibernateEnabled())
                    throw new HibernateNotSupportedException();
                if (!SetSuspendState(hibernate: true, forceCritical: false, disableWakeEvent: false))
                    throw new InvalidOperationException($"SetSuspendState(hibernate) failed: Win32 error {Marshal.GetLastWin32Error()}");
                break;
            case PowerAction.Lock:
                if (!LockWorkStation())
                    throw new InvalidOperationException($"LockWorkStation failed: Win32 error {Marshal.GetLastWin32Error()}");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    /// <summary>Stand-in for every PowerAction while SimulateDangerousActions is on: just pop Calculator.</summary>
    private static void RunStub(int delaySeconds)
    {
        if (delaySeconds > 0)
        {
            // Mirror shutdown.exe's /t semantics closely enough for timer testing: don't
            // block the caller, fire the stub after the delay on a background timer.
            _ = new System.Threading.Timer(_ => LaunchCalculator(), null, TimeSpan.FromSeconds(delaySeconds), Timeout.InfiniteTimeSpan);
            return;
        }

        LaunchCalculator();
    }

    private static void LaunchCalculator()
    {
        try
        {
            Process.Start(new ProcessStartInfo("calc.exe") { UseShellExecute = true });
        }
        catch
        {
            // Best-effort stub; swallow so a missing calc.exe doesn't crash the "safe" path.
        }
    }

    /// <summary>Cancels a pending shutdown/restart scheduled via shutdown.exe /t.</summary>
    public void CancelPendingShutdown() => RunShutdownExe("/a");

    private static void RunShutdownExe(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("shutdown.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        process?.WaitForExit(5000);
    }

    private static bool IsHibernateEnabled()
    {
        // Authoritative flag Windows itself maintains (same one `powercfg /hibernate on|off` toggles) —
        // more reliable than parsing `powercfg /a` text, which lists Hibernate under "not available"
        // with a reason rather than omitting it.
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power");
        return key?.GetValue("HibernateEnabled") is int enabled && enabled != 0;
    }
}

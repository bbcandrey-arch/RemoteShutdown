namespace RemoteShutdown.Agent.Tray;

/// <summary>
/// Не даёт запустить вторую копию Tray — раньше двойной клик по ярлыку (или ярлык +
/// отдельный ярлык "Настройки") несколько раз подряд плодил независимые процессы,
/// каждый со своим подключением к agent.db и попыткой занять единственное место в
/// SessionRelayServer (см. Core.Ipc) — если пользователь потом закрывал не ту копию,
/// переставала работать вся сессионная часть (громкость/медиа/блокировка/тачпад) или
/// само сопряжение, в зависимости от того, какая копия что успела захватить.
///
/// Именованный (не "Global\", сессии не нужен) Mutex + AutoReset-событие: вторая копия,
/// увидев занятый мьютекс, просто просит первую показать окно настроек и завершается
/// сама, вместо того чтобы открывать свою собственную независимую копию.
/// </summary>
internal static class SingleInstanceGuard
{
    private const string MutexName = "RemoteShutdownAgent.Tray.SingleInstance";
    private const string ShowSettingsEventName = "RemoteShutdownAgent.Tray.ShowSettings";

    /// <summary>Пытается стать единственным экземпляром. null, если экземпляр уже есть —
    /// вызывающий код в этом случае должен просто попросить его открыть настройки и выйти.</summary>
    public static Mutex? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew) return mutex;
        mutex.Dispose();
        return null;
    }

    /// <summary>Просит уже запущенный экземпляр показать окно настроек. Тихо ничего не
    /// делает, если тот почему-то не успел создать событие (крайне маловероятная гонка
    /// на старте) — вызывающий код всё равно тут же завершается сам.</summary>
    public static void SignalExistingInstance()
    {
        try
        {
            using var showSettingsEvent = EventWaitHandle.OpenExisting(ShowSettingsEventName);
            showSettingsEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
    }

    /// <summary>
    /// Создаёт именованное событие и Timer, который ненавязчиво (не блокируя UI-поток)
    /// проверяет его — сработает, если другая запущенная копия попросила показать
    /// настройки через SignalExistingInstance(). Timer, а не WaitHandle-колбэк на пуле
    /// потоков, — чтобы onSignal можно было безопасно трогать WinForms-контролы без
    /// ручного маршалинга на UI-поток.
    /// </summary>
    public static System.Windows.Forms.Timer WatchForSignal(Action onSignal)
    {
        var showSettingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += (_, _) =>
        {
            if (showSettingsEvent.WaitOne(0)) onSignal();
        };
        timer.Start();
        return timer;
    }
}

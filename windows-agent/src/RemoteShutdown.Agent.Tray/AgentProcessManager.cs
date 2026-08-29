using System.Diagnostics;

namespace RemoteShutdown.Agent.Tray;

/// <summary>
/// Запускает и отслеживает процесс RemoteShutdown.Agent.Api.exe как дочерний процесс
/// трея — так автозагрузка трея (см. AutostartService) поднимает весь агент целиком.
/// В будущем (docs/roadmap.md, "Windows Service") эту роль возьмёт на себя служба,
/// а трей будет только UI-компаньоном.
/// </summary>
public sealed class AgentProcessManager : IDisposable
{
    private Process? _process;

    public bool IsRunning => _process is { HasExited: false };

    /// <summary>
    /// Ищет RemoteShutdown.Agent.Api.exe двумя способами:
    /// 1) рядом с самим Tray.exe (сценарий дистрибутива — см. /distrib, оба exe лежат
    ///    в одной папке после `dotnet publish`);
    /// 2) если не нашли — поднимается от каталога трея до RemoteShutdown.Agent.sln и
    ///    смотрит в src/.../bin/{Release,Debug} (сценарий разработки из исходников).
    /// Возвращает null, если не нашёл ни там, ни там.
    /// </summary>
    public static string? FindAgentApiExecutable()
    {
        var besideTray = Path.Combine(AppContext.BaseDirectory, "RemoteShutdown.Agent.Api.exe");
        if (File.Exists(besideTray)) return besideTray;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RemoteShutdown.Agent.sln")))
            dir = dir.Parent;

        if (dir is null) return null;

        var binRoot = Path.Combine(dir.FullName, "src", "RemoteShutdown.Agent.Api", "bin");
        foreach (var configuration in new[] { "Release", "Debug" })
        {
            var candidate = Path.Combine(binRoot, configuration, "net9.0-windows", "RemoteShutdown.Agent.Api.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public void Start(string executablePath)
    {
        if (IsRunning) return;
        _process = Process.Start(new ProcessStartInfo(executablePath)
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Minimized,
        });
    }

    public void Stop()
    {
        if (_process is { HasExited: false })
        {
            try { _process.Kill(); } catch { /* уже завершился между проверкой и Kill — не критично */ }
        }
        _process = null;
    }

    public void Dispose() => Stop();
}

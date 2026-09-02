// Постоянно запущенный хост агента (docs/roadmap.md, "Windows Service") — стартует при
// загрузке ОС от имени LocalSystem, ДО того как кто-либо вошёл в Windows, поэтому
// команды выключения/перезагрузки/сна/гибернации работают всегда, пока ПК физически
// включён. Действия, которым физически нужен рабочий стол (громкость, медиа,
// блокировка, тачпад), эта же WebApplication просит выполнить Tray через
// SessionRelayServer — см. AgentHost.cs и RemoteShutdown.Agent.Core.Ipc.
//
// Тот же код (AgentHost.CreateApp) используется и в RemoteShutdown.Agent.Api.exe для
// интерактивного dev-запуска (`dotnet run`) — builder.Host.UseWindowsService(...) внутри
// ничего не делает, если процесс не был поднят диспетчером служб, поэтому этот же exe
// одинаково работает и из консоли (например, "sc start" в консоли для отладки).
RemoteShutdown.Agent.Api.AgentHost.CreateApp(args).Run();

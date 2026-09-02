using RemoteShutdown.Agent.Api;

// Интерактивный запуск (dev-цикл, `dotnet run --project src/RemoteShutdown.Agent.Api`) —
// в собранном дистрибутиве этот exe больше не участвует, роль постоянно запущенного
// хоста взял на себя RemoteShutdown.Agent.Service (см. docs/roadmap.md, "Windows
// Service"), использующий тот же AgentHost.CreateApp.
AgentHost.CreateApp(args).Run();

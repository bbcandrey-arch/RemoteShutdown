// Placeholder for Этап 7 of docs/architecture plan ("Windows Service"): this project will
// eventually host RemoteShutdown.Agent.Api's Kestrel server inside a proper Windows Service
// (Microsoft.Extensions.Hosting + UseWindowsService()) plus IPC to a tray-companion for
// Lock/UI, so the agent can run before any user logs in (Session 0 isolation — see
// PowerActionsService remarks).
//
// For the MVP (Этап 1-6), run RemoteShutdown.Agent.Api directly instead:
//   dotnet run --project src/RemoteShutdown.Agent.Api

var builder = Host.CreateApplicationBuilder(args);
var host = builder.Build();
host.Run();

using System;
using AppBlocker.Core.Data;
using AppBlocker.Core.Security;
using AppBlocker.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// Enable running as a Windows Service (runs as LocalSystem)
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "AppBlockerService";
});

// Configure services
builder.Services.AddSingleton<IPinSecurityService, PinSecurityService>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// Ensure SQLite database & tables exist at startup
try
{
    AppBlockerDbContext.InitializeDatabase();
    Console.WriteLine($"[AppBlocker.Service] Database initialized at: {AppBlockerDbContext.DefaultDbPath}");
}
catch (Exception ex)
{
    Console.WriteLine($"[AppBlocker.Service] Database initialization warning: {ex.Message}");
}

await host.RunAsync();

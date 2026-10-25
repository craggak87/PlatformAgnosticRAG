using System;
using CellAssistant.Application;
using CellAssistant.Application.Ports;
using CellAssistant.Infrastructure;
using CellAssistant.Infrastructure.Watcher;
using CellAssistant.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("service", "worker")
    .WriteTo.Console(new CompactJsonFormatter())
    .WriteTo.File(
        formatter: new CompactJsonFormatter(),
        path: "logs/worker-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14)
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting CellAssistant Worker host bootstrap...");
    var builder = new HostApplicationBuilder(args);

    // Serilog logging provider
    builder.Services.AddSerilog((services, loggerConfig) => loggerConfig
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("System", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("service", "worker")
        .WriteTo.Console(new CompactJsonFormatter())
        .WriteTo.File(
            formatter: new CompactJsonFormatter(),
            path: "logs/worker-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14));

    // Register Clean Architecture Layers
    builder.Services.AddApplicationServices(builder.Configuration);
    builder.Services.AddInfrastructure(builder.Configuration);

    // Register Hosted Service
    builder.Services.AddHostedService<IndexerWorker>();

    var host = builder.Build();
    Log.Information("CellAssistant Worker initialized successfully. Running daemon...");
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "CellAssistant Worker host terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

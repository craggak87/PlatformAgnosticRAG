using System;
using CellAssistant.Api.Middleware;
using CellAssistant.Application;
using CellAssistant.Application.Options;
using CellAssistant.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// Configure Server URLs if specified
if (!builder.Environment.IsEnvironment("Testing"))
{
    var aspNetCoreUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? builder.Configuration["ASPNETCORE_URLS"];
    bool hasExplicitCommandLinePort = args != null && args.Any(a =>
        a.StartsWith("--port", StringComparison.OrdinalIgnoreCase) ||
        a.StartsWith("-p", StringComparison.OrdinalIgnoreCase) ||
        a.StartsWith("/port", StringComparison.OrdinalIgnoreCase) ||
        a.StartsWith("--server:port", StringComparison.OrdinalIgnoreCase) ||
        a.StartsWith("/server:port", StringComparison.OrdinalIgnoreCase) ||
        a.StartsWith("--urls", StringComparison.OrdinalIgnoreCase) ||
        a.StartsWith("/urls", StringComparison.OrdinalIgnoreCase));

    if (string.IsNullOrWhiteSpace(aspNetCoreUrls) || hasExplicitCommandLinePort)
    {
        var serverSection = builder.Configuration.GetSection("server");
        var host = serverSection["host"] ?? "0.0.0.0";
        var portStr = builder.Configuration["port"] ?? serverSection["port"];
        if (int.TryParse(portStr, out int port) && port > 0)
        {
            builder.WebHost.UseUrls($"http://{host}:{port}");
        }
    }
}

// Add Application & Infrastructure Services
builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

// Add MVC Controllers
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            string correlationId = context.HttpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();
            var errors = context.ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .Where(msg => !string.IsNullOrWhiteSpace(msg))
                .ToList();

            string errorMessage = errors.Count > 0
                ? string.Join("; ", errors)
                : "Validation failed.";

            var errorDto = new CellAssistant.Application.DTOs.ApiErrorDto(errorMessage, correlationId);
            return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(errorDto);
        };
    });

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add Swagger / OpenAPI 3.1
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Cell Knowledge Assistant API",
        Version = "v1",
        Description = "Platform-Agnostic Industrial Knowledge Retrieval REST & SSE API for manufacturing cells"
    });
});

var app = builder.Build();

// Ensure SQLite schema (WAL mode) and Qdrant collection are initialized on startup
using (var scope = app.Services.CreateScope())
{
    var sqliteInitializer = scope.ServiceProvider.GetService<CellAssistant.Infrastructure.Persistence.Sqlite.ISqliteDatabaseInitializer>();
    if (sqliteInitializer is not null)
    {
        await sqliteInitializer.InitializeAsync();
    }

    if (!app.Environment.IsEnvironment("Testing"))
    {
        try
        {
            var vectorStore = scope.ServiceProvider.GetService<CellAssistant.Application.Ports.IVectorStore>();
            if (vectorStore is not null)
            {
                await vectorStore.InitializeCollectionAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Qdrant collection initialization deferred on API startup (will retry when reachable).");
        }
    }
}

// Global Structured Exception Handling Middleware
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Enable Swagger UI
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Cell Knowledge Assistant API v1");
    c.RoutePrefix = "swagger";
});

app.UseCors("AllowAll");

// Serve Default & Static Files from wwwroot
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
        ctx.Context.Response.Headers.Append("X-Frame-Options", "SAMEORIGIN");
        ctx.Context.Response.Headers.Append("Content-Security-Policy",
            "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-src 'self';");
    }
});

app.UseRouting();

app.MapControllers();

// SPA Fallback
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }

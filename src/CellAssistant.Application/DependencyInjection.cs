namespace CellAssistant.Application;

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CellAssistant.Application.Options;
using CellAssistant.Application.Ports;
using CellAssistant.Application.Services;
using CellAssistant.Domain.Services;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 1. Strongly-typed configuration options with DataAnnotations validation on startup
        services.AddOptions<ServerOptions>()
            .Bind(configuration.GetSection(ServerOptions.SectionName))
            .PostConfigure(options =>
            {
                var doubleUnderscoreHost = Environment.GetEnvironmentVariable("server__host")
                    ?? Environment.GetEnvironmentVariable("SERVER__HOST");
                if (string.IsNullOrWhiteSpace(doubleUnderscoreHost))
                {
                    var envHost = configuration["SERVER_HOST"] ?? Environment.GetEnvironmentVariable("SERVER_HOST");
                    if (!string.IsNullOrWhiteSpace(envHost))
                    {
                        options.Host = envHost;
                    }
                }

                var doubleUnderscorePort = Environment.GetEnvironmentVariable("server__port")
                    ?? Environment.GetEnvironmentVariable("SERVER__PORT");
                if (string.IsNullOrWhiteSpace(doubleUnderscorePort))
                {
                    var envPort = configuration["SERVER_PORT"] ?? Environment.GetEnvironmentVariable("SERVER_PORT");
                    if (!string.IsNullOrWhiteSpace(envPort) && int.TryParse(envPort, out int port) && port > 0)
                    {
                        options.Port = port;
                    }
                }
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<OllamaOptions>()
            .Bind(configuration.GetSection(OllamaOptions.SectionName))
            .PostConfigure(options =>
            {
                var doubleUnderscore = Environment.GetEnvironmentVariable("ollama__baseUrl")
                    ?? Environment.GetEnvironmentVariable("OLLAMA__BASEURL")
                    ?? Environment.GetEnvironmentVariable("OLLAMA__BASE_URL");
                if (string.IsNullOrWhiteSpace(doubleUnderscore))
                {
                    var envBaseUrl = configuration["OLLAMA_BASE_URL"] ?? Environment.GetEnvironmentVariable("OLLAMA_BASE_URL");
                    if (!string.IsNullOrWhiteSpace(envBaseUrl))
                    {
                        options.BaseUrl = envBaseUrl;
                    }
                }
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<QdrantOptions>()
            .Bind(configuration.GetSection(QdrantOptions.SectionName))
            .PostConfigure(options =>
            {
                var doubleUnderscore = Environment.GetEnvironmentVariable("qdrant__host")
                    ?? Environment.GetEnvironmentVariable("QDRANT__HOST");
                if (string.IsNullOrWhiteSpace(doubleUnderscore))
                {
                    var envHost = configuration["QDRANT_HOST"] ?? Environment.GetEnvironmentVariable("QDRANT_HOST");
                    if (!string.IsNullOrWhiteSpace(envHost))
                    {
                        options.Host = envHost;
                    }
                }
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<KnowledgeBaseOptions>()
            .Bind(configuration.GetSection(KnowledgeBaseOptions.SectionName))
            .PostConfigure(options =>
            {
                var doubleUnderscoreRoot = Environment.GetEnvironmentVariable("knowledgeBase__rootPath")
                    ?? Environment.GetEnvironmentVariable("KNOWLEDGEBASE__ROOTPATH")
                    ?? Environment.GetEnvironmentVariable("KNOWLEDGE_BASE__ROOT_PATH");
                if (string.IsNullOrWhiteSpace(doubleUnderscoreRoot))
                {
                    var envRootPath = configuration["KNOWLEDGE_BASE_ROOT_PATH"] ?? Environment.GetEnvironmentVariable("KNOWLEDGE_BASE_ROOT_PATH");
                    if (!string.IsNullOrWhiteSpace(envRootPath))
                    {
                        options.RootPath = envRootPath;
                    }
                }

                var doubleUnderscoreSqlite = Environment.GetEnvironmentVariable("knowledgeBase__sqlitePath")
                    ?? Environment.GetEnvironmentVariable("KNOWLEDGEBASE__SQLITEPATH")
                    ?? Environment.GetEnvironmentVariable("KNOWLEDGE_BASE__SQLITE_PATH");
                if (string.IsNullOrWhiteSpace(doubleUnderscoreSqlite))
                {
                    var envSqlitePath = configuration["KNOWLEDGE_BASE_SQLITE_PATH"] ?? Environment.GetEnvironmentVariable("KNOWLEDGE_BASE_SQLITE_PATH");
                    if (!string.IsNullOrWhiteSpace(envSqlitePath))
                    {
                        options.SqlitePath = envSqlitePath;
                    }
                }
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // 2. Domain Services
        services.AddSingleton<IIndustrialIdentifierExtractor, IndustrialIdentifierExtractor>();
        services.AddSingleton<IConfidenceCalculator, ConfidenceCalculator>();

        // 3. Application Services
        services.AddSingleton<ContextBuilder>();
        services.AddSingleton<IContextBuilder>(sp => sp.GetRequiredService<ContextBuilder>());
        services.AddScoped<IQueryService, ChatService>();
        services.AddScoped<ChatService>();

        return services;
    }
}

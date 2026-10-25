namespace CellAssistant.Infrastructure;

using System;
using CellAssistant.Application.Options;
using CellAssistant.Application.Ports;
using CellAssistant.Infrastructure.Adapters.Ollama;
using CellAssistant.Infrastructure.Persistence.Qdrant;
using CellAssistant.Infrastructure.Persistence.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using global::Qdrant.Client;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 1. SQLite Relational Persistence
        services.AddSingleton<ISqliteConnectionFactory, SqliteConnectionFactory>();
        services.AddSingleton<ISqliteDatabaseInitializer, SqliteDatabaseInitializer>();
        services.AddScoped<IDocumentRepository, SqliteDocumentRepository>();
        services.AddScoped<ISearchHistoryRepository, SqliteSearchHistoryRepository>();
        services.AddScoped<IIndexerStateRepository, SqliteIndexerStateRepository>();

        // 2. Qdrant Vector Store
        services.AddSingleton<QdrantClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<QdrantOptions>>().Value;
            return new QdrantClient(options.Host, options.GrpcPort, https: false);
        });
        services.AddSingleton<IQdrantClient>(sp => sp.GetRequiredService<QdrantClient>());
        services.AddSingleton<IVectorStore, QdrantVectorStore>();

        // 3. Ollama HttpClients & Adapters
        services.AddHttpClient<IEmbeddingService, OllamaEmbeddingService>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        });

        services.AddHttpClient<IChatService, OllamaChatService>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        });

        // 4. Document Parsers
        services.AddSingleton<Parsing.MarkdigYamlParser>();
        services.AddSingleton<Parsing.PdfPigParser>();
        services.AddSingleton<Parsing.PlainTextParser>();
        services.AddSingleton<IDocumentParser>(sp => sp.GetRequiredService<Parsing.MarkdigYamlParser>());
        services.AddSingleton<IDocumentParser>(sp => sp.GetRequiredService<Parsing.PdfPigParser>());
        services.AddSingleton<IDocumentParser>(sp => sp.GetRequiredService<Parsing.PlainTextParser>());

        // 5. Document Indexer
        services.AddScoped<IDocumentIndexer, Indexing.DocumentIndexer>();

        // 6. File Watcher Service
        services.AddSingleton<IFileWatcherService, Watcher.DebouncedFileWatcherService>();

        // 7. Health Check Service
        services.AddHttpClient<IHealthCheckService, Services.HealthCheckService>();

        return services;
    }

    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        return services.AddInfrastructure(configuration);
    }
}

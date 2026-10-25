namespace CellAssistant.UnitTests.Infrastructure;

using System;
using CellAssistant.Application.Options;
using CellAssistant.Application.Ports;
using CellAssistant.Infrastructure;
using CellAssistant.Infrastructure.Adapters.Ollama;
using CellAssistant.Infrastructure.Persistence.Qdrant;
using CellAssistant.Infrastructure.Persistence.Sqlite;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using global::Qdrant.Client;

public class DependencyInjectionTests
{
    private readonly IServiceCollection _services;
    private readonly IConfiguration _configuration;

    public DependencyInjectionTests()
    {
        _services = new ServiceCollection();
        _configuration = new ConfigurationBuilder().Build();

        // Register required options
        _services.AddSingleton(Options.Create(new KnowledgeBaseOptions { SqlitePath = ":memory:" }));
        _services.AddSingleton(Options.Create(new QdrantOptions { Host = "localhost", GrpcPort = 6334 }));
        _services.AddSingleton(Options.Create(new OllamaOptions { BaseUrl = "http://localhost:11434" }));
        _services.AddLogging();
    }

    [Fact]
    public void AddInfrastructure_RegistersAllRequiredPorts()
    {
        _services.AddInfrastructure(_configuration);
        var provider = _services.BuildServiceProvider();

        // SQLite singletons & repos
        provider.GetService<ISqliteConnectionFactory>().Should().NotBeNull();
        provider.GetService<ISqliteDatabaseInitializer>().Should().NotBeNull();
        provider.GetService<IDocumentRepository>().Should().NotBeNull();
        provider.GetService<ISearchHistoryRepository>().Should().NotBeNull();
        provider.GetService<IIndexerStateRepository>().Should().NotBeNull();

        // Qdrant
        provider.GetService<QdrantClient>().Should().NotBeNull();
        provider.GetService<IQdrantClient>().Should().NotBeNull();
        provider.GetService<IVectorStore>().Should().NotBeNull();

        // Ollama HTTP clients & adapters
        provider.GetService<IEmbeddingService>().Should().NotBeNull();
        provider.GetService<IChatService>().Should().NotBeNull();
    }

    [Fact]
    public void AddInfrastructure_ResolvesConcreteOllamaTypes()
    {
        _services.AddInfrastructure(_configuration);
        var provider = _services.BuildServiceProvider();

        provider.GetRequiredService<IEmbeddingService>().Should().BeOfType<OllamaEmbeddingService>();
        provider.GetRequiredService<IChatService>().Should().BeOfType<OllamaChatService>();
        provider.GetRequiredService<IVectorStore>().Should().BeOfType<QdrantVectorStore>();
        provider.GetRequiredService<IDocumentRepository>().Should().BeOfType<SqliteDocumentRepository>();
    }

    [Fact]
    public void AddInfrastructure_RegistersSqliteRepositoriesAsScoped()
    {
        _services.AddInfrastructure(_configuration);
        var provider = _services.BuildServiceProvider();

        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var repo1 = scope1.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var repo2 = scope2.ServiceProvider.GetRequiredService<IDocumentRepository>();

        repo1.Should().NotBeSameAs(repo2);
    }
}

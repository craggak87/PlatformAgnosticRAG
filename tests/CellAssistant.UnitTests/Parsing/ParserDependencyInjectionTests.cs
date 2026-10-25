namespace CellAssistant.UnitTests.Parsing;

using System.Collections.Generic;
using System.Linq;
using CellAssistant.Application.Options;
using CellAssistant.Application.Ports;
using CellAssistant.Infrastructure;
using CellAssistant.Infrastructure.Parsing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

public class ParserDependencyInjectionTests
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ParserDependencyInjectionTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Strongly-typed options required by infrastructure
        services.AddSingleton(Options.Create(new KnowledgeBaseOptions { RootPath = "/tmp" }));
        services.AddSingleton(Options.Create(new QdrantOptions { Host = "localhost", GrpcPort = 6334 }));
        services.AddSingleton(Options.Create(new OllamaOptions { BaseUrl = "http://localhost:11434" }));

        services.AddInfrastructure();

        var sp = services.BuildServiceProvider();
        _scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public void AddInfrastructure_RegistersAllThreeParsers()
    {
        using var scope = _scopeFactory.CreateScope();
        var parsers = scope.ServiceProvider.GetRequiredService<IEnumerable<IDocumentParser>>().ToList();

        parsers.Should().HaveCount(3);
        parsers.Should().ContainSingle(p => p is MarkdigYamlParser);
        parsers.Should().ContainSingle(p => p is PdfPigParser);
        parsers.Should().ContainSingle(p => p is PlainTextParser);
    }

    [Fact]
    public void AddInfrastructure_ResolvesConcreteParserTypes()
    {
        using var scope = _scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<MarkdigYamlParser>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<PdfPigParser>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<PlainTextParser>().Should().NotBeNull();
    }

    [Fact]
    public void AddInfrastructure_ResolvesDocumentIndexerAndFileWatcher()
    {
        using var scope = _scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<IDocumentIndexer>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IFileWatcherService>().Should().NotBeNull();
    }
}

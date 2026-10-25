namespace CellAssistant.UnitTests.Application;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CellAssistant.Application;
using CellAssistant.Application.Options;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

public class ConfigurationRemediationTests
{
    [Fact]
    public void AddApplicationServices_WhenSingleUnderscoreVariablesPresent_BindsFallbackOptions()
    {
        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["QDRANT_HOST"] = "remote-qdrant",
            ["OLLAMA_BASE_URL"] = "http://remote-ollama:11434",
            ["KNOWLEDGE_BASE_ROOT_PATH"] = "/custom/vault",
            ["KNOWLEDGE_BASE_SQLITE_PATH"] = "/custom/db/state.db",
            ["SERVER_PORT"] = "9090",
            ["SERVER_HOST"] = "192.168.1.50"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemoryConfig)
            .Build();

        var services = new ServiceCollection();
        services.AddApplicationServices(configuration);
        var provider = services.BuildServiceProvider();

        var qdrant = provider.GetRequiredService<IOptions<QdrantOptions>>().Value;
        qdrant.Host.Should().Be("remote-qdrant");

        var ollama = provider.GetRequiredService<IOptions<OllamaOptions>>().Value;
        ollama.BaseUrl.Should().Be("http://remote-ollama:11434");

        var kb = provider.GetRequiredService<IOptions<KnowledgeBaseOptions>>().Value;
        kb.RootPath.Should().Be("/custom/vault");
        kb.SqlitePath.Should().Be("/custom/db/state.db");

        var server = provider.GetRequiredService<IOptions<ServerOptions>>().Value;
        server.Port.Should().Be(9090);
        server.Host.Should().Be("192.168.1.50");
    }

    [Fact]
    public void AddApplicationServices_WhenEnvironmentVariablesSet_BindsCorrectly()
    {
        var originalQdrantHost = Environment.GetEnvironmentVariable("QDRANT_HOST");
        var originalOllamaUrl = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL");
        var originalKbRoot = Environment.GetEnvironmentVariable("KNOWLEDGE_BASE_ROOT_PATH");
        var originalKbSqlite = Environment.GetEnvironmentVariable("KNOWLEDGE_BASE_SQLITE_PATH");

        try
        {
            Environment.SetEnvironmentVariable("QDRANT_HOST", "env-qdrant");
            Environment.SetEnvironmentVariable("OLLAMA_BASE_URL", "http://env-ollama:11434");
            Environment.SetEnvironmentVariable("KNOWLEDGE_BASE_ROOT_PATH", "/data/KnowledgeBase");
            Environment.SetEnvironmentVariable("KNOWLEDGE_BASE_SQLITE_PATH", "/data/state/metadata.db");

            var configuration = new ConfigurationBuilder().Build();
            var services = new ServiceCollection();
            services.AddApplicationServices(configuration);
            var provider = services.BuildServiceProvider();

            var qdrant = provider.GetRequiredService<IOptions<QdrantOptions>>().Value;
            qdrant.Host.Should().Be("env-qdrant");

            var ollama = provider.GetRequiredService<IOptions<OllamaOptions>>().Value;
            ollama.BaseUrl.Should().Be("http://env-ollama:11434");

            var kb = provider.GetRequiredService<IOptions<KnowledgeBaseOptions>>().Value;
            kb.RootPath.Should().Be("/data/KnowledgeBase");
            kb.SqlitePath.Should().Be("/data/state/metadata.db");
        }
        finally
        {
            Environment.SetEnvironmentVariable("QDRANT_HOST", originalQdrantHost);
            Environment.SetEnvironmentVariable("OLLAMA_BASE_URL", originalOllamaUrl);
            Environment.SetEnvironmentVariable("KNOWLEDGE_BASE_ROOT_PATH", originalKbRoot);
            Environment.SetEnvironmentVariable("KNOWLEDGE_BASE_SQLITE_PATH", originalKbSqlite);
        }
    }

    [Fact]
    public void AppSettingsJson_ServerPort_Is8080()
    {
        var appsettingsPath = Path.Combine(AppContext.BaseDirectory, "../../../../../src/CellAssistant.Api/appsettings.json");
        File.Exists(appsettingsPath).Should().BeTrue($"appsettings.json must exist at {appsettingsPath}");

        var json = File.ReadAllText(appsettingsPath);
        using var doc = JsonDocument.Parse(json);
        var port = doc.RootElement.GetProperty("server").GetProperty("port").GetInt32();
        port.Should().Be(8080, "appsettings.json port must be configured to 8080 matching EXPOSE 8080 and config.yaml");
    }

    [Fact]
    public void DockerCompose_ContainsBothSingleAndDoubleUnderscoreConventions()
    {
        var dockerComposePath = Path.Combine(AppContext.BaseDirectory, "../../../../../docker-compose.yml");
        File.Exists(dockerComposePath).Should().BeTrue($"docker-compose.yml must exist at {dockerComposePath}");

        var content = File.ReadAllText(dockerComposePath);

        // API checks
        content.Should().Contain("QDRANT_HOST=qdrant");
        content.Should().Contain("qdrant__host=qdrant");
        content.Should().Contain("OLLAMA_BASE_URL=http://host.docker.internal:11434");
        content.Should().Contain("ollama__baseUrl=http://host.docker.internal:11434");
        content.Should().Contain("KNOWLEDGE_BASE_ROOT_PATH=/data/KnowledgeBase");
        content.Should().Contain("knowledgeBase__rootPath=/data/KnowledgeBase");
        content.Should().Contain("KNOWLEDGE_BASE_SQLITE_PATH=/data/state/metadata.db");
        content.Should().Contain("knowledgeBase__sqlitePath=/data/state/metadata.db");
        content.Should().Contain("SERVER_PORT=8080");
        content.Should().Contain("server__port=8080");
    }

    [Theory]
    [InlineData("deployment/docker/Dockerfile.api")]
    [InlineData("deployment/docker/Dockerfile.worker")]
    public void Dockerfiles_ContainNonRootUserAndOwnership(string relativePath)
    {
        var dockerfilePath = Path.Combine(AppContext.BaseDirectory, "../../../../../", relativePath);
        File.Exists(dockerfilePath).Should().BeTrue($"Dockerfile must exist at {dockerfilePath}");

        var content = File.ReadAllText(dockerfilePath);
        content.Should().Contain("USER $APP_UID", "Container runtime stage must specify USER $APP_UID");
        content.Should().Contain("chown -R $APP_UID:$APP_UID /data/state /data/KnowledgeBase /app/logs /app",
            "Container must grant ownership of runtime directories to $APP_UID");
    }
}

namespace CellAssistant.UnitTests.Application;

using System.ComponentModel.DataAnnotations;
using CellAssistant.Application.Options;
using FluentAssertions;
using Xunit;

public class OptionsValidationTests
{
    private static IList<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(model);
        Validator.TryValidateObject(model, context, results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void ServerOptions_ValidValues_ShouldPassValidation()
    {
        var options = new ServerOptions
        {
            Host = "0.0.0.0",
            Port = 8080
        };

        var results = Validate(options);
        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(70000)]
    public void ServerOptions_InvalidPort_ShouldFailValidation(int invalidPort)
    {
        var options = new ServerOptions
        {
            Host = "127.0.0.1",
            Port = invalidPort
        };

        var results = Validate(options);
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(ServerOptions.Port)));
    }

    [Fact]
    public void ServerOptions_NullOrEmptyHost_ShouldFailValidation()
    {
        var options = new ServerOptions
        {
            Host = null!,
            Port = 8080
        };

        var results = Validate(options);
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(ServerOptions.Host)));
    }

    [Fact]
    public void OllamaOptions_ValidValues_ShouldPassValidation()
    {
        var options = new OllamaOptions
        {
            BaseUrl = "http://localhost:11434",
            ChatModel = "gemma4:e4b-it-q4_K_M",
            FallbackChatModel = "qwen3.8:latest",
            EmbeddingModel = "embeddinggemma-2:latest",
            RequestTimeoutSeconds = 120
        };

        var results = Validate(options);
        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData("not-a-valid-url")]
    [InlineData("://missing-scheme")]
    [InlineData("localhost:11434")]
    public void OllamaOptions_InvalidUrl_ShouldFailValidation(string invalidUrl)
    {
        var options = new OllamaOptions
        {
            BaseUrl = invalidUrl,
            ChatModel = "gemma4:e4b-it-q4_K_M",
            FallbackChatModel = "qwen3.8:latest",
            EmbeddingModel = "embeddinggemma-2:latest",
            RequestTimeoutSeconds = 120
        };

        var results = Validate(options);
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(OllamaOptions.BaseUrl)));
    }

    [Fact]
    public void OllamaOptions_MissingModels_ShouldFailValidation()
    {
        var options = new OllamaOptions
        {
            BaseUrl = "http://localhost:11434",
            ChatModel = null!,
            FallbackChatModel = null!,
            EmbeddingModel = null!,
            RequestTimeoutSeconds = 120
        };

        var results = Validate(options);
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(OllamaOptions.ChatModel)));
        results.Should().Contain(r => r.MemberNames.Contains(nameof(OllamaOptions.FallbackChatModel)));
        results.Should().Contain(r => r.MemberNames.Contains(nameof(OllamaOptions.EmbeddingModel)));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(1000)]
    public void OllamaOptions_InvalidTimeout_ShouldFailValidation(int invalidTimeout)
    {
        var options = new OllamaOptions
        {
            BaseUrl = "http://localhost:11434",
            ChatModel = "gemma4:e4b-it-q4_K_M",
            FallbackChatModel = "qwen3.8:latest",
            EmbeddingModel = "embeddinggemma-2:latest",
            RequestTimeoutSeconds = invalidTimeout
        };

        var results = Validate(options);
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(OllamaOptions.RequestTimeoutSeconds)));
    }

    [Fact]
    public void QdrantOptions_ValidValues_ShouldPassValidation()
    {
        var options = new QdrantOptions
        {
            Host = "localhost",
            HttpPort = 6333,
            GrpcPort = 6334,
            CollectionName = "cell-knowledge",
            VectorSize = 768
        };

        var results = Validate(options);
        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData(64)]
    [InlineData(8192)]
    public void QdrantOptions_InvalidVectorSize_ShouldFailValidation(int invalidSize)
    {
        var options = new QdrantOptions
        {
            Host = "localhost",
            HttpPort = 6333,
            GrpcPort = 6334,
            CollectionName = "cell-knowledge",
            VectorSize = invalidSize
        };

        var results = Validate(options);
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(QdrantOptions.VectorSize)));
    }

    [Fact]
    public void KnowledgeBaseOptions_ValidValues_ShouldPassValidation()
    {
        var options = new KnowledgeBaseOptions
        {
            RootPath = "./KnowledgeBase",
            SqlitePath = "./data/metadata.db",
            MaxChunkWords = 500,
            ChunkOverlapWords = 100,
            WatcherDebounceMs = 500
        };

        var results = Validate(options);
        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData(10, 100, 500)]
    [InlineData(5000, 100, 500)]
    [InlineData(500, 600, 500)]
    [InlineData(500, 100, 50)]
    [InlineData(500, 100, 10000)]
    public void KnowledgeBaseOptions_InvalidChunksAndDebounce_ShouldFailValidation(
        int maxChunk, int overlap, int debounce)
    {
        var options = new KnowledgeBaseOptions
        {
            RootPath = "./KnowledgeBase",
            SqlitePath = "./data/metadata.db",
            MaxChunkWords = maxChunk,
            ChunkOverlapWords = overlap,
            WatcherDebounceMs = debounce
        };

        var results = Validate(options);
        results.Should().NotBeEmpty();
    }
}

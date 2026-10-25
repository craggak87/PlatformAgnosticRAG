namespace CellAssistant.UnitTests.Application;

using System.ComponentModel.DataAnnotations;
using CellAssistant.Application;
using CellAssistant.Application.DTOs;
using CellAssistant.Application.Options;
using CellAssistant.Application.Services;
using CellAssistant.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

public class EmpiricalStressTests
{
    private static IList<ValidationResult> ValidateModel(object model)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(model);
        Validator.TryValidateObject(model, context, results, validateAllProperties: true);
        return results;
    }

    // =========================================================================
    // 1. Empirical Verification: Options Validation
    // =========================================================================

    [Theory]
    [InlineData(1, true)]
    [InlineData(8080, true)]
    [InlineData(65535, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(65536, false)]
    [InlineData(99999, false)]
    public void ServerOptions_Port_BoundaryTesting(int port, bool shouldPass)
    {
        var options = new ServerOptions { Host = "127.0.0.1", Port = port };
        var results = ValidateModel(options);

        if (shouldPass)
        {
            results.Should().BeEmpty();
        }
        else
        {
            results.Should().Contain(r => r.MemberNames.Contains(nameof(ServerOptions.Port)));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ServerOptions_Host_RequiredTesting(string? host)
    {
        var options = new ServerOptions { Host = host!, Port = 8080 };
        var results = ValidateModel(options);
        results.Should().Contain(r => r.MemberNames.Contains(nameof(ServerOptions.Host)));
    }

    [Theory]
    [InlineData("http://localhost:11434", true)]
    [InlineData("https://ollama.internal.net:8443", true)]
    [InlineData("not-a-valid-url", false)]
    [InlineData("ftp://invalid-scheme", true)] // UrlAttribute accepts absolute URI with schemes
    [InlineData("://broken-url", false)]
    [InlineData("localhost:11434", false)] // Missing scheme
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OllamaOptions_BaseUrl_UrlValidationTesting(string? url, bool shouldPass)
    {
        var options = new OllamaOptions
        {
            BaseUrl = url!,
            ChatModel = "gemma4:e4b-it-q4_K_M",
            FallbackChatModel = "qwen3.8:latest",
            EmbeddingModel = "embeddinggemma-2:latest",
            RequestTimeoutSeconds = 120
        };
        var results = ValidateModel(options);

        if (shouldPass)
        {
            results.Should().NotContain(r => r.MemberNames.Contains(nameof(OllamaOptions.BaseUrl)));
        }
        else
        {
            results.Should().Contain(r => r.MemberNames.Contains(nameof(OllamaOptions.BaseUrl)));
        }
    }

    [Theory]
    [InlineData(null, "fallback", "embed")]
    [InlineData("chat", null, "embed")]
    [InlineData("chat", "fallback", null)]
    [InlineData("", "fallback", "embed")]
    [InlineData("chat", "", "embed")]
    [InlineData("chat", "fallback", "")]
    public void OllamaOptions_Models_RequiredTesting(string? chat, string? fallback, string? embed)
    {
        var options = new OllamaOptions
        {
            BaseUrl = "http://localhost:11434",
            ChatModel = chat!,
            FallbackChatModel = fallback!,
            EmbeddingModel = embed!,
            RequestTimeoutSeconds = 120
        };
        var results = ValidateModel(options);
        results.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(120, true)]
    [InlineData(600, true)]
    [InlineData(9, false)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(601, false)]
    [InlineData(1000, false)]
    public void OllamaOptions_RequestTimeoutSeconds_BoundaryTesting(int timeout, bool shouldPass)
    {
        var options = new OllamaOptions
        {
            BaseUrl = "http://localhost:11434",
            ChatModel = "gemma4:e4b-it-q4_K_M",
            FallbackChatModel = "qwen3.8:latest",
            EmbeddingModel = "embeddinggemma-2:latest",
            RequestTimeoutSeconds = timeout
        };
        var results = ValidateModel(options);

        if (shouldPass)
        {
            results.Should().BeEmpty();
        }
        else
        {
            results.Should().Contain(r => r.MemberNames.Contains(nameof(OllamaOptions.RequestTimeoutSeconds)));
        }
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(6333, true)]
    [InlineData(65535, true)]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    [InlineData(65536, false)]
    public void QdrantOptions_Ports_BoundaryTesting(int port, bool shouldPass)
    {
        var options = new QdrantOptions
        {
            Host = "localhost",
            HttpPort = port,
            GrpcPort = port,
            CollectionName = "cell-knowledge",
            VectorSize = 768
        };
        var results = ValidateModel(options);

        if (shouldPass)
        {
            results.Should().BeEmpty();
        }
        else
        {
            results.Should().Contain(r => r.MemberNames.Contains(nameof(QdrantOptions.HttpPort)));
            results.Should().Contain(r => r.MemberNames.Contains(nameof(QdrantOptions.GrpcPort)));
        }
    }

    [Theory]
    [InlineData(128, true)]
    [InlineData(768, true)]
    [InlineData(4096, true)]
    [InlineData(127, false)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(4097, false)]
    [InlineData(8192, false)]
    public void QdrantOptions_VectorSize_BoundaryTesting(int vectorSize, bool shouldPass)
    {
        var options = new QdrantOptions
        {
            Host = "localhost",
            HttpPort = 6333,
            GrpcPort = 6334,
            CollectionName = "cell-knowledge",
            VectorSize = vectorSize
        };
        var results = ValidateModel(options);

        if (shouldPass)
        {
            results.Should().BeEmpty();
        }
        else
        {
            results.Should().Contain(r => r.MemberNames.Contains(nameof(QdrantOptions.VectorSize)));
        }
    }

    [Theory]
    [InlineData(null, "cell-knowledge")]
    [InlineData("", "cell-knowledge")]
    [InlineData("localhost", null)]
    [InlineData("localhost", "")]
    public void QdrantOptions_HostAndCollection_RequiredTesting(string? host, string? collection)
    {
        var options = new QdrantOptions
        {
            Host = host!,
            HttpPort = 6333,
            GrpcPort = 6334,
            CollectionName = collection!,
            VectorSize = 768
        };
        var results = ValidateModel(options);
        results.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(50, true)]
    [InlineData(500, true)]
    [InlineData(2000, true)]
    [InlineData(49, false)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(2001, false)]
    public void KnowledgeBaseOptions_MaxChunkWords_BoundaryTesting(int maxWords, bool shouldPass)
    {
        var options = new KnowledgeBaseOptions
        {
            RootPath = "./KnowledgeBase",
            SqlitePath = "./data/metadata.db",
            MaxChunkWords = maxWords,
            ChunkOverlapWords = 100,
            WatcherDebounceMs = 500
        };
        var results = ValidateModel(options);

        if (shouldPass)
        {
            results.Should().BeEmpty();
        }
        else
        {
            results.Should().Contain(r => r.MemberNames.Contains(nameof(KnowledgeBaseOptions.MaxChunkWords)));
        }
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(100, true)]
    [InlineData(500, true)]
    [InlineData(-1, false)]
    [InlineData(501, false)]
    public void KnowledgeBaseOptions_ChunkOverlapWords_BoundaryTesting(int overlap, bool shouldPass)
    {
        var options = new KnowledgeBaseOptions
        {
            RootPath = "./KnowledgeBase",
            SqlitePath = "./data/metadata.db",
            MaxChunkWords = 500,
            ChunkOverlapWords = overlap,
            WatcherDebounceMs = 500
        };
        var results = ValidateModel(options);

        if (shouldPass)
        {
            results.Should().BeEmpty();
        }
        else
        {
            results.Should().Contain(r => r.MemberNames.Contains(nameof(KnowledgeBaseOptions.ChunkOverlapWords)));
        }
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(500, true)]
    [InlineData(5000, true)]
    [InlineData(99, false)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(5001, false)]
    public void KnowledgeBaseOptions_WatcherDebounceMs_BoundaryTesting(int debounce, bool shouldPass)
    {
        var options = new KnowledgeBaseOptions
        {
            RootPath = "./KnowledgeBase",
            SqlitePath = "./data/metadata.db",
            MaxChunkWords = 500,
            ChunkOverlapWords = 100,
            WatcherDebounceMs = debounce
        };
        var results = ValidateModel(options);

        if (shouldPass)
        {
            results.Should().BeEmpty();
        }
        else
        {
            results.Should().Contain(r => r.MemberNames.Contains(nameof(KnowledgeBaseOptions.WatcherDebounceMs)));
        }
    }

    [Fact]
    public void Options_Validation_CatchesAllCorruptedConfigurationProperties()
    {
        var invalidServer = new ServerOptions { Host = "", Port = 99999 };
        var serverResults = ValidateModel(invalidServer);
        serverResults.Should().Contain(r => r.MemberNames.Contains(nameof(ServerOptions.Host)));
        serverResults.Should().Contain(r => r.MemberNames.Contains(nameof(ServerOptions.Port)));

        var invalidOllama = new OllamaOptions
        {
            BaseUrl = "broken-url-format",
            ChatModel = "",
            FallbackChatModel = "",
            EmbeddingModel = "",
            RequestTimeoutSeconds = 5
        };
        var ollamaResults = ValidateModel(invalidOllama);
        ollamaResults.Should().Contain(r => r.MemberNames.Contains(nameof(OllamaOptions.BaseUrl)));
        ollamaResults.Should().Contain(r => r.MemberNames.Contains(nameof(OllamaOptions.ChatModel)));
        ollamaResults.Should().Contain(r => r.MemberNames.Contains(nameof(OllamaOptions.FallbackChatModel)));
        ollamaResults.Should().Contain(r => r.MemberNames.Contains(nameof(OllamaOptions.EmbeddingModel)));
        ollamaResults.Should().Contain(r => r.MemberNames.Contains(nameof(OllamaOptions.RequestTimeoutSeconds)));

        var invalidQdrant = new QdrantOptions
        {
            Host = "",
            HttpPort = 0,
            GrpcPort = 70000,
            CollectionName = "",
            VectorSize = 50
        };
        var qdrantResults = ValidateModel(invalidQdrant);
        qdrantResults.Should().Contain(r => r.MemberNames.Contains(nameof(QdrantOptions.Host)));
        qdrantResults.Should().Contain(r => r.MemberNames.Contains(nameof(QdrantOptions.HttpPort)));
        qdrantResults.Should().Contain(r => r.MemberNames.Contains(nameof(QdrantOptions.GrpcPort)));
        qdrantResults.Should().Contain(r => r.MemberNames.Contains(nameof(QdrantOptions.CollectionName)));
        qdrantResults.Should().Contain(r => r.MemberNames.Contains(nameof(QdrantOptions.VectorSize)));

        var invalidKb = new KnowledgeBaseOptions
        {
            RootPath = "",
            SqlitePath = "",
            MaxChunkWords = 10,
            ChunkOverlapWords = 600,
            WatcherDebounceMs = 20
        };
        var kbResults = ValidateModel(invalidKb);
        kbResults.Should().Contain(r => r.MemberNames.Contains(nameof(KnowledgeBaseOptions.RootPath)));
        kbResults.Should().Contain(r => r.MemberNames.Contains(nameof(KnowledgeBaseOptions.SqlitePath)));
        kbResults.Should().Contain(r => r.MemberNames.Contains(nameof(KnowledgeBaseOptions.MaxChunkWords)));
        kbResults.Should().Contain(r => r.MemberNames.Contains(nameof(KnowledgeBaseOptions.ChunkOverlapWords)));
        kbResults.Should().Contain(r => r.MemberNames.Contains(nameof(KnowledgeBaseOptions.WatcherDebounceMs)));
    }

    // =========================================================================
    // 2. Empirical Verification: ContextBuilder Stress Testing
    // =========================================================================

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void ContextBuilder_EmptyOrNullChunks_FormatsFallbackPromptAndEmptySources(int? chunkCount)
    {
        var builder = new ContextBuilder();
        var chunks = chunkCount == null ? null : new List<RetrievedChunk>();

        var prompt = builder.BuildPrompt("What is the fault code?", chunks);
        prompt.Should().Contain(ContextBuilder.SystemPrompt);
        prompt.Should().Contain("Context:\nNo relevant documentation found.");
        prompt.Should().Contain("Question: What is the fault code?");

        var sources = builder.ExtractSources(chunks);
        sources.Should().BeEmpty();
    }

    [Fact]
    public void ContextBuilder_LargeChunkCount_EnforcesTopKLimitAndDescendingScoreOrder()
    {
        var builder = new ContextBuilder();
        var docId = Guid.NewGuid();

        // Generate 100 chunks with distinct scores from 0.01 to 1.00
        var chunks = Enumerable.Range(1, 100)
            .Select(i => new RetrievedChunk(
                Content: $"[Chunk content #{i:D3}]",
                SimilarityScore: i / 100.0,
                ExactIdentifierMatch: false,
                Source: new SourceReference(docId, "Manual.pdf", "Manuals/Manual.pdf", "Manuals", i, $"Section {i}")
            ))
            .ToList();

        // Build prompt with topK = 5
        var prompt = builder.BuildPrompt("Explain gripper error", chunks, topK: 5);

        // Chunks #100, #099, #098, #097, #096 should be included
        prompt.Should().Contain("[Chunk content #100]");
        prompt.Should().Contain("[Chunk content #099]");
        prompt.Should().Contain("[Chunk content #098]");
        prompt.Should().Contain("[Chunk content #097]");
        prompt.Should().Contain("[Chunk content #096]");

        // Lower score chunks should NOT be included in the prompt
        prompt.Should().NotContain("[Chunk content #095]");
        prompt.Should().NotContain("[Chunk content #050]");
        prompt.Should().NotContain("[Chunk content #001]");

        // Ensure order: chunk 100 appears before chunk 99
        prompt.IndexOf("[Chunk content #100]").Should().BeLessThan(prompt.IndexOf("[Chunk content #099]"));
        prompt.IndexOf("[Chunk content #099]").Should().BeLessThan(prompt.IndexOf("[Chunk content #098]"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-5)]
    public void ContextBuilder_ZeroOrNegativeTopK_ReturnsPromptWithoutChunks(int topK)
    {
        var builder = new ContextBuilder();
        var docId = Guid.NewGuid();
        var chunks = new[]
        {
            new RetrievedChunk("Chunk 1", 0.95, false, new SourceReference(docId, "M.pdf", "M.pdf", "M", 1, null))
        };

        var prompt = builder.BuildPrompt("Question", chunks, topK: topK);
        prompt.Should().NotContain("Chunk 1");
        prompt.Should().Contain("Context:");
        prompt.Should().Contain("Question: Question");
    }

    [Fact]
    public void ContextBuilder_HighStressPayload_HandlesLargeContentStringsGracefully()
    {
        var builder = new ContextBuilder();
        var docId = Guid.NewGuid();

        // Generate a 100,000 character context payload
        var massiveContent = new string('A', 100_000);
        var chunks = new[]
        {
            new RetrievedChunk(massiveContent, 0.95, true, new SourceReference(docId, "Heavy.pdf", "Manuals/Heavy.pdf", "Manuals", 1, "DeepSection"))
        };

        var prompt = builder.BuildPrompt("Summarize the document", chunks, topK: 1);
        prompt.Length.Should().BeGreaterThan(100_000);
        prompt.Should().Contain(massiveContent);
    }

    [Fact]
    public void ContextBuilder_MaxLengthQuestion_FormatsProperly()
    {
        var builder = new ContextBuilder();
        // 2000 characters matching QueryRequestDto MaxLength
        var maxQuestion = new string('Q', 2000);

        var prompt = builder.BuildPrompt(maxQuestion, Array.Empty<RetrievedChunk>());
        prompt.Should().Contain($"Question: {maxQuestion}");
    }

    [Fact]
    public void ContextBuilder_DeduplicatesMultipleOverlappingSources()
    {
        var builder = new ContextBuilder();
        var docId1 = Guid.NewGuid();
        var docId2 = Guid.NewGuid();

        var chunks = new[]
        {
            new RetrievedChunk("Content 1", 0.95, false, new SourceReference(docId1, "Doc1.pdf", "Manuals/Doc1.pdf", "Manuals", 5, "Heading A")),
            new RetrievedChunk("Content 2", 0.90, false, new SourceReference(docId1, "Doc1.pdf", "Manuals/Doc1.pdf", "Manuals", 5, "Heading A")), // exact duplicate source
            new RetrievedChunk("Content 3", 0.85, false, new SourceReference(docId1, "Doc1.pdf", "Manuals/Doc1.pdf", "Manuals", 6, "Heading B")), // different page
            new RetrievedChunk("Content 4", 0.80, false, new SourceReference(docId2, "Doc2.pdf", "Manuals/Doc2.pdf", "Manuals", 5, "Heading A")), // different document
            new RetrievedChunk("Content 5", 0.75, false, new SourceReference(docId2, "Doc2.pdf", "Manuals/Doc2.pdf", "Manuals", null, null))      // null page & heading
        };

        var sources = builder.ExtractSources(chunks);
        sources.Should().HaveCount(4); // 5 chunks reduced to 4 distinct sources
        sources.Count(s => s.DocumentId == docId1 && s.PageNumber == 5).Should().Be(1);
    }
}

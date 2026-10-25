namespace CellAssistant.UnitTests.Application;

using CellAssistant.Application.Services;
using CellAssistant.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

public class ContextBuilderTests
{
    private readonly ContextBuilder _builder = new();

    private static RetrievedChunk CreateChunk(string content, double score, Guid docId, int? page = null, string? heading = null)
    {
        return new RetrievedChunk(
            Content: content,
            SimilarityScore: score,
            ExactIdentifierMatch: false,
            Source: new SourceReference(docId, "Manual.pdf", "Manuals/Manual.pdf", "Manuals", page, heading)
        );
    }

    [Fact]
    public void SystemPrompt_MatchesSpecificationVerbatim()
    {
        ContextBuilder.SystemPrompt.Should().Contain("You are an engineering assistant.");
        ContextBuilder.SystemPrompt.Should().Contain("Answer only with supplied context.");
        ContextBuilder.SystemPrompt.Should().Contain("Do not invent information.");
        ContextBuilder.SystemPrompt.Should().Contain("Always cite sources.");
        ContextBuilder.SystemPrompt.Should().Contain(ContextBuilder.InsufficientInformationFallback);
    }

    [Fact]
    public void BuildPrompt_WithValidChunks_IncludesContextHeadersAndQuestion()
    {
        var docId = Guid.NewGuid();
        var chunks = new[]
        {
            CreateChunk("[Document: Gripper.md | Equipment: ROBOT01 | Section: Faults]\nSensor cable damaged.", 0.89, docId, heading: "Faults"),
            CreateChunk("[Document: Manual.pdf | Folder: Manuals | Page: 17]\nCheck I:0/15 input.", 0.81, docId, page: 17)
        };

        var prompt = _builder.BuildPrompt("What causes gripper timeout?", chunks);

        prompt.Should().Contain("Sensor cable damaged.");
        prompt.Should().Contain("Check I:0/15 input.");
        prompt.Should().Contain("Question: What causes gripper timeout?");
        prompt.Should().Contain(ContextBuilder.SystemPrompt);
    }

    [Fact]
    public void BuildPrompt_WithEmptyChunks_IncludesNoRelevantDocumentationFound()
    {
        var prompt = _builder.BuildPrompt("Unknown question", Array.Empty<RetrievedChunk>());

        prompt.Should().Contain("No relevant documentation found.");
        prompt.Should().Contain("Question: Unknown question");
    }

    [Fact]
    public void BuildPrompt_TopKCap_LimitsNumberOfIncludedChunks()
    {
        var docId = Guid.NewGuid();
        var chunks = new[]
        {
            CreateChunk("Chunk 1 (low)", 0.50, docId),
            CreateChunk("Chunk 2 (mid)", 0.75, docId),
            CreateChunk("Chunk 3 (high)", 0.95, docId),
            CreateChunk("Chunk 4 (highest)", 0.99, docId),
            CreateChunk("Chunk 5 (good)", 0.88, docId)
        };

        var prompt = _builder.BuildPrompt("Test question", chunks, topK: 3);

        prompt.Should().Contain("Chunk 4 (highest)");
        prompt.Should().Contain("Chunk 3 (high)");
        prompt.Should().Contain("Chunk 5 (good)");
        prompt.Should().NotContain("Chunk 1 (low)");
        prompt.Should().NotContain("Chunk 2 (mid)");
    }

    [Fact]
    public void ExtractSources_ShouldDeduplicateAndMapSources()
    {
        var docId = Guid.NewGuid();
        var chunks = new[]
        {
            CreateChunk("Text 1", 0.90, docId, page: 12, heading: "Section A"),
            CreateChunk("Text 2", 0.85, docId, page: 12, heading: "Section A"), // duplicate source
            CreateChunk("Text 3", 0.80, docId, page: 14, heading: "Section B")
        };

        var sources = _builder.ExtractSources(chunks);

        sources.Should().HaveCount(2);
        sources.Should().Contain(s => s.DocumentId == docId && s.PageNumber == 12 && s.SectionHeading == "Section A");
        sources.Should().Contain(s => s.DocumentId == docId && s.PageNumber == 14 && s.SectionHeading == "Section B");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void BuildPrompt_NullOrEmptyQuestion_ThrowsArgumentException(string? question)
    {
        var act = () => _builder.BuildPrompt(question!, Array.Empty<RetrievedChunk>());
        act.Should().Throw<ArgumentException>();
    }
}

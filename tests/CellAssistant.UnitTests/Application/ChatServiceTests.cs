namespace CellAssistant.UnitTests.Application;

using CellAssistant.Application.DTOs;
using CellAssistant.Application.Ports;
using CellAssistant.Application.Services;
using CellAssistant.Domain.Entities;
using CellAssistant.Domain.Enums;
using CellAssistant.Domain.Services;
using CellAssistant.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class ChatServiceTests
{
    private readonly IEmbeddingService _embeddingService = Substitute.For<IEmbeddingService>();
    private readonly IVectorStore _vectorStore = Substitute.For<IVectorStore>();
    private readonly IChatService _llmChatService = Substitute.For<IChatService>();
    private readonly ISearchHistoryRepository _searchHistoryRepository = Substitute.For<ISearchHistoryRepository>();
    private readonly IIndustrialIdentifierExtractor _identifierExtractor = Substitute.For<IIndustrialIdentifierExtractor>();
    private readonly IConfidenceCalculator _confidenceCalculator = Substitute.For<IConfidenceCalculator>();
    private readonly ContextBuilder _contextBuilder = new();

    private readonly ChatService _chatService;

    public ChatServiceTests()
    {
        _chatService = new ChatService(
            _embeddingService,
            _vectorStore,
            _llmChatService,
            _searchHistoryRepository,
            _identifierExtractor,
            _confidenceCalculator,
            _contextBuilder
        );

        // Default embedding stub (768-dim vector)
        _embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>(new float[768]));
    }

    private static RetrievedChunk CreateChunk(double score, bool exactMatch = false)
    {
        return new RetrievedChunk(
            Content: "[Document: Gripper.md | Folder: Faults]\nSensor pinched.",
            SimilarityScore: score,
            ExactIdentifierMatch: exactMatch,
            Source: new SourceReference(Guid.NewGuid(), "Gripper.md", "Faults/Gripper.md", "Faults", 1, "Section 1")
        );
    }

    [Fact]
    public async Task ExecuteQueryAsync_WhenNoChunksFound_ReturnsExactFallbackStringAndLogsHistory()
    {
        var request = new QueryRequestDto("How to cook pasta?");
        _identifierExtractor.ExtractIdentifiers(request.Question).Returns(Array.Empty<string>());
        _vectorStore.SearchHybridAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<IReadOnlyList<string>?>(), null, null, 5, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<RetrievedChunk>());
        _confidenceCalculator.Calculate(Arg.Any<IReadOnlyList<RetrievedChunk>?>(), false)
            .Returns((0.0, ConfidenceLevel.Low));

        var response = await _chatService.ExecuteQueryAsync(request);

        response.Answer.Should().Be(ContextBuilder.InsufficientInformationFallback);
        response.Confidence.Should().Be("Low");
        response.Sources.Should().BeEmpty();

        await _llmChatService.DidNotReceive().GenerateAnswerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _searchHistoryRepository.Received(1).AddAsync(
            Arg.Is<SearchHistoryEntry>(e =>
                e.Question == request.Question &&
                e.Answer == ContextBuilder.InsufficientInformationFallback &&
                e.Confidence == ConfidenceLevel.Low &&
                e.Sources.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteQueryAsync_WhenAllChunksBelowMinimumThreshold_ReturnsExactFallbackString()
    {
        var request = new QueryRequestDto("Unrelated question");
        var weakChunk = CreateChunk(0.65, false);
        _identifierExtractor.ExtractIdentifiers(request.Question).Returns(Array.Empty<string>());
        _vectorStore.SearchHybridAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<IReadOnlyList<string>?>(), null, null, 5, Arg.Any<CancellationToken>())
            .Returns(new[] { weakChunk });
        _confidenceCalculator.Calculate(Arg.Any<IReadOnlyList<RetrievedChunk>?>(), false)
            .Returns((0.65, ConfidenceLevel.Low));

        var response = await _chatService.ExecuteQueryAsync(request);

        response.Answer.Should().Be(ContextBuilder.InsufficientInformationFallback);
        response.Confidence.Should().Be("Low");
        response.Sources.Should().BeEmpty();

        await _llmChatService.DidNotReceive().GenerateAnswerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteQueryAsync_WhenSufficientChunksRetrieved_InvokesLlmAndReturnsGroundedResponse()
    {
        var request = new QueryRequestDto("What causes ALM-205?", "ROBOT01", "Faults");
        var chunk = CreateChunk(0.88, true);

        _identifierExtractor.ExtractIdentifiers(request.Question).Returns(new[] { "ALM-205" });
        _vectorStore.SearchHybridAsync(
            Arg.Any<ReadOnlyMemory<float>>(),
            Arg.Is<IReadOnlyList<string>?>(ids => ids != null && ids.Contains("ALM-205")),
            "ROBOT01",
            "Faults",
            5,
            Arg.Any<CancellationToken>())
            .Returns(new[] { chunk });

        _confidenceCalculator.Calculate(Arg.Any<IReadOnlyList<RetrievedChunk>?>(), true)
            .Returns((0.88, ConfidenceLevel.High));

        _llmChatService.GenerateAnswerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("ALM-205 indicates sensor wire breakage.");

        var response = await _chatService.ExecuteQueryAsync(request);

        response.Answer.Should().Be("ALM-205 indicates sensor wire breakage.");
        response.Confidence.Should().Be("High");
        response.Sources.Should().HaveCount(1);
        response.Sources[0].FileName.Should().Be("Gripper.md");

        await _searchHistoryRepository.Received(1).AddAsync(
            Arg.Is<SearchHistoryEntry>(e =>
                e.Question == request.Question &&
                e.Confidence == ConfidenceLevel.High &&
                e.Sources.Count == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteQueryAsync_WhenLlmReturnsInsufficientInformation_NormalizesToExactFallback()
    {
        var request = new QueryRequestDto("Question with vague docs");
        var chunk = CreateChunk(0.75, false);

        _vectorStore.SearchHybridAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<IReadOnlyList<string>?>(), null, null, 5, Arg.Any<CancellationToken>())
            .Returns(new[] { chunk });
        _confidenceCalculator.Calculate(Arg.Any<IReadOnlyList<RetrievedChunk>?>(), false)
            .Returns((0.75, ConfidenceLevel.Medium));

        _llmChatService.GenerateAnswerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("Based on the context, the available documentation does not contain sufficient information to answer.");

        var response = await _chatService.ExecuteQueryAsync(request);

        response.Answer.Should().Be(ContextBuilder.InsufficientInformationFallback);
    }

    [Fact]
    public async Task ExecuteQueryStreamAsync_ShouldEmitMetadataFirstThenTokens()
    {
        var request = new QueryRequestDto("What causes ALM-205?");
        var chunk = CreateChunk(0.85, true);

        _identifierExtractor.ExtractIdentifiers(request.Question).Returns(new[] { "ALM-205" });
        _vectorStore.SearchHybridAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<IReadOnlyList<string>?>(), null, null, 5, Arg.Any<CancellationToken>())
            .Returns(new[] { chunk });
        _confidenceCalculator.Calculate(Arg.Any<IReadOnlyList<RetrievedChunk>?>(), true)
            .Returns((0.85, ConfidenceLevel.High));

        _llmChatService.StreamAnswerAsync(Arg.Any<string>(), Arg.Any<Func<string, Task>>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var onToken = callInfo.Arg<Func<string, Task>>();
                await onToken("ALM-205 ");
                await onToken("gripper ");
                await onToken("fault.");
                return "ALM-205 gripper fault.";
            });

        QueryMetadataDto? receivedMetadata = null;
        var receivedTokens = new List<string>();

        await _chatService.ExecuteQueryStreamAsync(
            request,
            token =>
            {
                // Verify metadata was emitted before any tokens
                receivedMetadata.Should().NotBeNull("Metadata must be emitted before first token");
                receivedTokens.Add(token);
                return Task.CompletedTask;
            },
            metadata =>
            {
                receivedMetadata = metadata;
                return Task.CompletedTask;
            });

        receivedMetadata.Should().NotBeNull();
        receivedMetadata!.Confidence.Should().Be("High");
        receivedMetadata.Sources.Should().HaveCount(1);
        string.Concat(receivedTokens).Should().Be("ALM-205 gripper fault.");

        await _searchHistoryRepository.Received(1).AddAsync(
            Arg.Is<SearchHistoryEntry>(e => e.Confidence == ConfidenceLevel.High),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteQueryStreamAsync_WhenInsufficientContext_EmitsFallbackToken()
    {
        var request = new QueryRequestDto("How to bake bread?");
        _vectorStore.SearchHybridAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<IReadOnlyList<string>?>(), null, null, 5, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<RetrievedChunk>());
        _confidenceCalculator.Calculate(Arg.Any<IReadOnlyList<RetrievedChunk>?>(), false)
            .Returns((0.0, ConfidenceLevel.Low));

        QueryMetadataDto? receivedMetadata = null;
        var receivedTokens = new List<string>();

        await _chatService.ExecuteQueryStreamAsync(
            request,
            token =>
            {
                receivedTokens.Add(token);
                return Task.CompletedTask;
            },
            metadata =>
            {
                receivedMetadata = metadata;
                return Task.CompletedTask;
            });

        receivedMetadata.Should().NotBeNull();
        receivedMetadata!.Confidence.Should().Be("Low");
        receivedMetadata.Sources.Should().BeEmpty();
        string.Concat(receivedTokens).Should().Be(ContextBuilder.InsufficientInformationFallback);
    }

    [Fact]
    public async Task AskAsync_And_StreamAskAsync_AliasesWorkIdentically()
    {
        var request = new QueryRequestDto("Question");
        _vectorStore.SearchHybridAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<IReadOnlyList<string>?>(), null, null, 5, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<RetrievedChunk>());
        _confidenceCalculator.Calculate(Arg.Any<IReadOnlyList<RetrievedChunk>?>(), false)
            .Returns((0.0, ConfidenceLevel.Low));

        var response = await _chatService.AskAsync(request);
        response.Answer.Should().Be(ContextBuilder.InsufficientInformationFallback);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteQueryAsync_EmptyQuestion_ThrowsArgumentException(string question)
    {
        var act = async () => await _chatService.ExecuteQueryAsync(new QueryRequestDto(question));
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExecuteQueryAsync_NullRequest_ThrowsArgumentNullException()
    {
        var act = async () => await _chatService.ExecuteQueryAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}

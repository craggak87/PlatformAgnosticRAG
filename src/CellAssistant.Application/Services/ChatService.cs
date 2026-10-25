namespace CellAssistant.Application.Services;

using CellAssistant.Application.DTOs;
using CellAssistant.Application.Ports;
using CellAssistant.Domain.Entities;
using CellAssistant.Domain.Enums;
using CellAssistant.Domain.Services;
using CellAssistant.Domain.ValueObjects;

/// <summary>
/// Core RAG orchestration service executing industrial identifier extraction,
/// query embedding, hybrid retrieval, confidence calculation, context assembly,
/// and LLM completion with strict grounding.
/// </summary>
public class ChatService : IQueryService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly IChatService _chatService;
    private readonly ISearchHistoryRepository _searchHistoryRepository;
    private readonly IIndustrialIdentifierExtractor _identifierExtractor;
    private readonly IConfidenceCalculator _confidenceCalculator;
    private readonly IContextBuilder _contextBuilder;

    public const double MinimumSufficientScoreThreshold = 0.70;

    public ChatService(
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        IChatService chatService,
        ISearchHistoryRepository searchHistoryRepository,
        IIndustrialIdentifierExtractor identifierExtractor,
        IConfidenceCalculator confidenceCalculator,
        IContextBuilder contextBuilder)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
        _searchHistoryRepository = searchHistoryRepository ?? throw new ArgumentNullException(nameof(searchHistoryRepository));
        _identifierExtractor = identifierExtractor ?? throw new ArgumentNullException(nameof(identifierExtractor));
        _confidenceCalculator = confidenceCalculator ?? throw new ArgumentNullException(nameof(confidenceCalculator));
        _contextBuilder = contextBuilder ?? throw new ArgumentNullException(nameof(contextBuilder));
    }

    public async Task<QueryResponseDto> ExecuteQueryAsync(
        QueryRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var question = request.Question?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Question cannot be empty.", nameof(request));

        // Step 1: Industrial Identifier Extraction
        var identifiers = _identifierExtractor.ExtractIdentifiers(question);

        // Step 2: Query Embedding Generation
        var queryVector = await _embeddingService.GenerateEmbeddingAsync(question, cancellationToken);

        // Step 3: Hybrid Retrieval in Qdrant
        var chunks = await _vectorStore.SearchHybridAsync(
            queryVector,
            identifiers,
            request.EquipmentIdFilter,
            request.FolderFilter,
            limit: request.TopK.GetValueOrDefault(5),
            cancellationToken);

        // Step 4: Confidence Calculation
        var hasExactMatch = chunks.Any(c => c.ExactIdentifierMatch);
        var (score, confidenceLevel) = _confidenceCalculator.Calculate(chunks, hasExactMatch);
        var confidenceString = confidenceLevel.ToString();

        // Step 5: Insufficient Context Gate
        var maxScore = chunks.Count > 0 ? chunks.Max(c => c.SimilarityScore) : 0.0;
        if (chunks.Count == 0 || maxScore < MinimumSufficientScoreThreshold)
        {
            stopwatch.Stop();
            var fallbackResponse = new QueryResponseDto(
                ContextBuilder.InsufficientInformationFallback,
                ConfidenceLevel.Low.ToString(),
                Array.Empty<SourceReferenceDto>(),
                ConfidenceScore: 0.0,
                RetrievedSources: Array.Empty<SourceReferenceDto>(),
                ExecutionTimeMs: stopwatch.ElapsedMilliseconds);

            await PersistHistoryAsync(
                question, 
                fallbackResponse.Answer, 
                ConfidenceLevel.Low, 
                Array.Empty<SourceReference>(), 
                cancellationToken);

            return fallbackResponse;
        }

        var sources = _contextBuilder.ExtractSources(chunks);
        var domainSources = chunks
            .Select(c => c.Source)
            .GroupBy(s => new { s.DocumentId, s.PageNumber, s.SectionHeading })
            .Select(g => g.First())
            .ToList();

        // Step 6: Grounded Prompt Assembly
        var prompt = _contextBuilder.BuildPrompt(question, chunks);

        // Step 7: Chat Completion via Ollama
        var answer = await _chatService.GenerateAnswerAsync(prompt, cancellationToken);

        if (string.IsNullOrWhiteSpace(answer) ||
            answer.Contains("does not contain sufficient information", StringComparison.OrdinalIgnoreCase))
        {
            answer = ContextBuilder.InsufficientInformationFallback;
        }

        stopwatch.Stop();
        var response = new QueryResponseDto(
            answer,
            confidenceString,
            sources,
            ConfidenceScore: score,
            RetrievedSources: sources,
            ExecutionTimeMs: stopwatch.ElapsedMilliseconds);

        // Step 8: Persist to Search History
        await PersistHistoryAsync(question, response.Answer, confidenceLevel, domainSources, cancellationToken);

        return response;
    }

    public async Task ExecuteQueryStreamAsync(
        QueryRequestDto request,
        Func<string, Task> onToken,
        Func<QueryMetadataDto, Task> onMetadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(onToken);
        ArgumentNullException.ThrowIfNull(onMetadata);

        var question = request.Question?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Question cannot be empty.", nameof(request));

        // Step 1: Industrial Identifier Extraction
        var identifiers = _identifierExtractor.ExtractIdentifiers(question);

        // Step 2: Query Embedding Generation
        var queryVector = await _embeddingService.GenerateEmbeddingAsync(question, cancellationToken);

        // Step 3: Hybrid Retrieval in Qdrant
        var chunks = await _vectorStore.SearchHybridAsync(
            queryVector,
            identifiers,
            request.EquipmentIdFilter,
            request.FolderFilter,
            limit: request.TopK.GetValueOrDefault(5),
            cancellationToken);

        // Step 4: Confidence Calculation
        var hasExactMatch = chunks.Any(c => c.ExactIdentifierMatch);
        var (score, confidenceLevel) = _confidenceCalculator.Calculate(chunks, hasExactMatch);
        var confidenceString = confidenceLevel.ToString();

        // Step 5: Insufficient Context Gate
        var maxScore = chunks.Count > 0 ? chunks.Max(c => c.SimilarityScore) : 0.0;
        if (chunks.Count == 0 || maxScore < MinimumSufficientScoreThreshold)
        {
            var emptySources = Array.Empty<SourceReferenceDto>();
            await onMetadata(new QueryMetadataDto(
                ConfidenceLevel.Low.ToString(),
                emptySources,
                ConfidenceScore: 0.0,
                RetrievedSources: emptySources));
            await onToken(ContextBuilder.InsufficientInformationFallback);

            await PersistHistoryAsync(
                question, 
                ContextBuilder.InsufficientInformationFallback, 
                ConfidenceLevel.Low, 
                Array.Empty<SourceReference>(), 
                cancellationToken);
            return;
        }

        var sources = _contextBuilder.ExtractSources(chunks);
        var domainSources = chunks
            .Select(c => c.Source)
            .GroupBy(s => new { s.DocumentId, s.PageNumber, s.SectionHeading })
            .Select(g => g.First())
            .ToList();

        // Emit Metadata first via SSE
        await onMetadata(new QueryMetadataDto(
            confidenceString,
            sources,
            ConfidenceScore: score,
            RetrievedSources: sources));

        // Step 6: Grounded Prompt Assembly
        var prompt = _contextBuilder.BuildPrompt(question, chunks);

        // Step 7: Stream Answer Tokens via Ollama
        var answer = await _chatService.StreamAnswerAsync(prompt, onToken, cancellationToken);

        if (string.IsNullOrWhiteSpace(answer) ||
            answer.Contains("does not contain sufficient information", StringComparison.OrdinalIgnoreCase))
        {
            answer = ContextBuilder.InsufficientInformationFallback;
        }

        // Step 8: Persist to Search History
        await PersistHistoryAsync(question, answer, confidenceLevel, domainSources, cancellationToken);
    }

    /// <summary>
    /// Convenience alias for ExecuteQueryAsync.
    /// </summary>
    public Task<QueryResponseDto> AskAsync(
        QueryRequestDto request,
        CancellationToken cancellationToken = default) => ExecuteQueryAsync(request, cancellationToken);

    /// <summary>
    /// Convenience alias for ExecuteQueryStreamAsync.
    /// </summary>
    public Task StreamAskAsync(
        QueryRequestDto request,
        Func<string, Task> onToken,
        Func<QueryMetadataDto, Task> onMetadata,
        CancellationToken cancellationToken = default) => ExecuteQueryStreamAsync(request, onToken, onMetadata, cancellationToken);

    private async Task PersistHistoryAsync(
        string question,
        string answer,
        ConfidenceLevel confidence,
        IReadOnlyList<SourceReference> sources,
        CancellationToken cancellationToken)
    {
        try
        {
            var entry = new SearchHistoryEntry
            {
                Id = Guid.NewGuid(),
                Question = question,
                Answer = answer,
                Confidence = confidence,
                Sources = sources.ToList(),
                AskedUtc = DateTimeOffset.UtcNow
            };

            await _searchHistoryRepository.AddAsync(entry, cancellationToken);
        }
        catch
        {
            // Search history logging failures do not fail user query execution
        }
    }
}

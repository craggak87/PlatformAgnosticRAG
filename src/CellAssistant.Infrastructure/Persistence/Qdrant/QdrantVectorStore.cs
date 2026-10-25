namespace CellAssistant.Infrastructure.Persistence.Qdrant;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.Options;
using CellAssistant.Application.Ports;
using CellAssistant.Domain.Entities;
using CellAssistant.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using global::Qdrant.Client;
using global::Qdrant.Client.Grpc;

public class QdrantVectorStore : IVectorStore
{
    private readonly IQdrantClient _client;
    private readonly QdrantOptions _options;
    private readonly ILogger<QdrantVectorStore> _logger;

    public QdrantVectorStore(
        IQdrantClient client,
        IOptions<QdrantOptions> options,
        ILogger<QdrantVectorStore> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InitializeCollectionAsync(CancellationToken cancellationToken = default)
    {
        var collectionName = _options.CollectionName;

        // 1. Check if collection exists
        bool exists = await _client.CollectionExistsAsync(collectionName, cancellationToken);
        if (!exists)
        {
            _logger.LogInformation("Creating Qdrant collection '{Collection}' (Dims: {Dims}, Metric: Cosine)",
                collectionName, _options.VectorSize);

            await _client.CreateCollectionAsync(
                collectionName: collectionName,
                vectorsConfig: new VectorParams
                {
                    Size = (ulong)_options.VectorSize,
                    Distance = Distance.Cosine
                },
                cancellationToken: cancellationToken);
        }

        // 2. Inspect existing payload schema
        var collectionInfo = await _client.GetCollectionInfoAsync(collectionName, cancellationToken);
        var existingSchema = collectionInfo.PayloadSchema;

        // 3. Create full-text index on 'content' if not present
        if (!existingSchema.ContainsKey("content"))
        {
            var textParams = new TextIndexParams
            {
                Tokenizer = TokenizerType.Word,
                MinTokenLen = 2,
                MaxTokenLen = 20,
                Lowercase = true
            };

            await _client.CreatePayloadIndexAsync(
                collectionName: collectionName,
                fieldName: "content",
                schemaType: PayloadSchemaType.Text,
                indexParams: new PayloadIndexParams { TextIndexParams = textParams },
                wait: true,
                cancellationToken: cancellationToken);
        }

        // 4. Create the 6 keyword indexes if not present
        string[] keywordFields = ["documentId", "equipmentId", "faultId", "alarmId", "tags", "folder"];
        foreach (var field in keywordFields)
        {
            if (!existingSchema.ContainsKey(field))
            {
                await _client.CreatePayloadIndexAsync(
                    collectionName: collectionName,
                    fieldName: field,
                    schemaType: PayloadSchemaType.Keyword,
                    wait: true,
                    cancellationToken: cancellationToken);
            }
        }
    }

    public async Task UpsertVectorsAsync(IReadOnlyList<VectorPoint> points, CancellationToken cancellationToken = default)
    {
        if (points == null || points.Count == 0) return;

        var pointStructs = new List<PointStruct>(points.Count);

        foreach (var pt in points)
        {
            if (pt.Vector == null || pt.Vector.Length != _options.VectorSize)
            {
                throw new ArgumentException(
                    $"Point '{pt.ChunkId}' vector length {pt.Vector?.Length ?? 0} does not match required size {_options.VectorSize}.",
                    nameof(points));
            }

            for (int i = 0; i < pt.Vector.Length; i++)
            {
                float val = pt.Vector[i];
                if (float.IsNaN(val) || float.IsInfinity(val))
                {
                    throw new ArgumentException($"Vector for chunk '{pt.ChunkId}' contains invalid float at index {i} ({val}).", nameof(points));
                }
            }

            var ps = new PointStruct
            {
                Id = pt.ChunkId == Guid.Empty ? Guid.NewGuid() : pt.ChunkId,
                Vectors = pt.Vector
            };

            // Populate payload with null-safe protobuf mapping
            ps.Payload["documentId"] = pt.DocumentId.ToString();
            ps.Payload["fileName"] = pt.FileName ?? string.Empty;
            ps.Payload["relativePath"] = (pt.RelativePath ?? string.Empty).Replace('\\', '/');
            ps.Payload["folder"] = pt.Folder ?? string.Empty;

            ps.Payload["page"] = pt.PageNumber.HasValue
                ? pt.PageNumber.Value
                : new Value { NullValue = NullValue.NullValue };

            ps.Payload["sectionHeading"] = !string.IsNullOrEmpty(pt.SectionHeading)
                ? pt.SectionHeading
                : new Value { NullValue = NullValue.NullValue };

            ps.Payload["equipmentId"] = !string.IsNullOrEmpty(pt.EquipmentId)
                ? pt.EquipmentId
                : new Value { NullValue = NullValue.NullValue };

            ps.Payload["faultId"] = !string.IsNullOrEmpty(pt.FaultId)
                ? pt.FaultId
                : new Value { NullValue = NullValue.NullValue };

            ps.Payload["alarmId"] = !string.IsNullOrEmpty(pt.AlarmId)
                ? pt.AlarmId
                : new Value { NullValue = NullValue.NullValue };

            ps.Payload["content"] = pt.Content ?? string.Empty;

            var tagsList = new ListValue();
            if (pt.Tags != null)
            {
                foreach (var tag in pt.Tags)
                {
                    if (!string.IsNullOrWhiteSpace(tag))
                        tagsList.Values.Add(new Value { StringValue = tag.Trim() });
                }
            }
            ps.Payload["tags"] = new Value { ListValue = tagsList };

            pointStructs.Add(ps);
        }

        // Upsert in batches of 100
        const int batchSize = 100;
        for (int i = 0; i < pointStructs.Count; i += batchSize)
        {
            var batch = pointStructs.Skip(i).Take(batchSize).ToList();
            await _client.UpsertAsync(_options.CollectionName, batch, wait: true, cancellationToken: cancellationToken);
        }
    }

    public async Task DeleteByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        if (documentId == Guid.Empty) return;

        var filter = new Filter();
        filter.Must.Add(Conditions.MatchKeyword("documentId", documentId.ToString()));

        await _client.DeleteAsync(
            collectionName: _options.CollectionName,
            filter: filter,
            wait: true,
            cancellationToken: cancellationToken);
    }

    public async Task<long> GetVectorCountAsync(CancellationToken cancellationToken = default)
    {
        var count = await _client.CountAsync(_options.CollectionName, exact: true, cancellationToken: cancellationToken);
        return (long)count;
    }

    public Task<IReadOnlyList<RetrievedChunk>> SearchHybridAsync(
        ReadOnlyMemory<float> queryVector,
        string? equipmentId,
        string? folder,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return SearchHybridAsync(queryVector, exactKeywords: null, equipmentId, folder, limit, cancellationToken);
    }

    public async Task<IReadOnlyList<RetrievedChunk>> SearchHybridAsync(
        ReadOnlyMemory<float> queryVector,
        IReadOnlyList<string>? exactKeywords,
        string? equipmentId,
        string? folder,
        int limit,
        CancellationToken cancellationToken = default)
    {
        int effectiveLimit = limit <= 0 ? 5 : limit;
        if (queryVector.Length != _options.VectorSize)
            return Array.Empty<RetrievedChunk>();

        var filter = new Filter();
        bool hasFilter = false;

        if (!string.IsNullOrWhiteSpace(equipmentId))
        {
            filter.Must.Add(Conditions.MatchKeyword("equipmentId", equipmentId.Trim()));
            hasFilter = true;
        }

        if (!string.IsNullOrWhiteSpace(folder))
        {
            filter.Must.Add(Conditions.MatchKeyword("folder", folder.Trim()));
            hasFilter = true;
        }

        var qdrantFilter = hasFilter ? filter : null;

        int candidateLimit = Math.Max(effectiveLimit * 3, 20);
#pragma warning disable CS0618
        var searchResults = await _client.SearchAsync(
            collectionName: _options.CollectionName,
            vector: queryVector,
            filter: qdrantFilter,
            limit: (ulong)candidateLimit,
            cancellationToken: cancellationToken);
#pragma warning restore CS0618

        if (searchResults == null || searchResults.Count == 0)
            return Array.Empty<RetrievedChunk>();

        var candidateChunks = new List<(RetrievedChunk Chunk, double RankingScore)>();
        const double minimumRelevanceThreshold = 0.50;

        foreach (var scoredPoint in searchResults)
        {
            double similarityScore = scoredPoint.Score;
            var payload = scoredPoint.Payload;

            string docIdStr = payload.TryGetValue("documentId", out var docIdVal) ? docIdVal.StringValue : string.Empty;
            _ = Guid.TryParse(docIdStr, out var documentId);

            string fileName = payload.TryGetValue("fileName", out var fnVal) ? fnVal.StringValue : string.Empty;
            string relativePath = payload.TryGetValue("relativePath", out var rpVal) ? rpVal.StringValue : string.Empty;
            string folderName = payload.TryGetValue("folder", out var fVal) ? fVal.StringValue : string.Empty;

            int? pageNumber = payload.TryGetValue("page", out var pVal) && pVal.KindCase == Value.KindOneofCase.IntegerValue
                ? (int)pVal.IntegerValue
                : null;

            string? sectionHeading = payload.TryGetValue("sectionHeading", out var shVal) && shVal.KindCase == Value.KindOneofCase.StringValue
                ? shVal.StringValue
                : null;

            string? equipmentIdVal = payload.TryGetValue("equipmentId", out var eqVal) && eqVal.KindCase == Value.KindOneofCase.StringValue
                ? eqVal.StringValue
                : null;

            string? faultIdVal = payload.TryGetValue("faultId", out var fltVal) && fltVal.KindCase == Value.KindOneofCase.StringValue
                ? fltVal.StringValue
                : null;

            string? alarmIdVal = payload.TryGetValue("alarmId", out var almVal) && almVal.KindCase == Value.KindOneofCase.StringValue
                ? almVal.StringValue
                : null;

            string content = payload.TryGetValue("content", out var cVal) ? cVal.StringValue : string.Empty;

            bool exactMatch = false;
            if (exactKeywords != null && exactKeywords.Count > 0)
            {
                foreach (var kw in exactKeywords)
                {
                    if (string.IsNullOrWhiteSpace(kw)) continue;
                    string cleanKw = kw.Trim();

                    if ((equipmentIdVal != null && equipmentIdVal.Equals(cleanKw, StringComparison.OrdinalIgnoreCase)) ||
                        (faultIdVal != null && faultIdVal.Equals(cleanKw, StringComparison.OrdinalIgnoreCase)) ||
                        (alarmIdVal != null && alarmIdVal.Equals(cleanKw, StringComparison.OrdinalIgnoreCase)) ||
                        content.Contains(cleanKw, StringComparison.OrdinalIgnoreCase))
                    {
                        exactMatch = true;
                        break;
                    }
                }
            }

            double rankingScore = exactMatch ? similarityScore + 0.15 : similarityScore;

            if (similarityScore < minimumRelevanceThreshold && !exactMatch)
            {
                continue;
            }

            var source = new SourceReference(
                DocumentId: documentId,
                FileName: fileName,
                RelativePath: relativePath,
                Folder: folderName,
                PageNumber: pageNumber,
                SectionHeading: sectionHeading);

            var chunk = new RetrievedChunk(
                Content: content,
                SimilarityScore: similarityScore,
                ExactIdentifierMatch: exactMatch,
                Source: source);

            candidateChunks.Add((chunk, rankingScore));
        }

        return candidateChunks
            .OrderByDescending(x => x.RankingScore)
            .ThenByDescending(x => x.Chunk.SimilarityScore)
            .Take(effectiveLimit)
            .Select(x => x.Chunk)
            .ToList();
    }
}

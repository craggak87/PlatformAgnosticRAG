namespace CellAssistant.UnitTests.Infrastructure.Persistence;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.Options;
using CellAssistant.Domain.Entities;
using CellAssistant.Domain.ValueObjects;
using CellAssistant.Infrastructure.Persistence.Qdrant;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using global::Qdrant.Client;
using global::Qdrant.Client.Grpc;

public class QdrantVectorStoreTests
{
    private readonly QdrantOptions _options;
    private readonly IOptions<QdrantOptions> _optionsWrapper;

    public QdrantVectorStoreTests()
    {
        _options = new QdrantOptions
        {
            Host = "localhost",
            GrpcPort = 6334,
            CollectionName = "cell-knowledge",
            VectorSize = 768
        };
        _optionsWrapper = Options.Create(_options);
    }

    [Fact]
    public async Task UpsertVectorsAsync_EmptyList_ReturnsImmediately()
    {
        var client = Substitute.For<IQdrantClient>();
        var store = new QdrantVectorStore(client, _optionsWrapper, NullLogger<QdrantVectorStore>.Instance);

        await store.UpsertVectorsAsync(Array.Empty<VectorPoint>());

        await client.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default!, default, default, default, default);
    }

    [Fact]
    public async Task UpsertVectorsAsync_IncorrectVectorDimension_ThrowsArgumentException()
    {
        var client = Substitute.For<IQdrantClient>();
        var store = new QdrantVectorStore(client, _optionsWrapper, NullLogger<QdrantVectorStore>.Instance);

        var badPoint = new VectorPoint
        {
            ChunkId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Vector = new float[512] // Expected 768
        };

        var act = async () => await store.UpsertVectorsAsync(new[] { badPoint });
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*does not match required size 768*");
    }

    [Fact]
    public async Task UpsertVectorsAsync_VectorContainsNaN_ThrowsArgumentException()
    {
        var client = Substitute.For<IQdrantClient>();
        var store = new QdrantVectorStore(client, _optionsWrapper, NullLogger<QdrantVectorStore>.Instance);

        var nanVector = new float[768];
        nanVector[10] = float.NaN;

        var badPoint = new VectorPoint
        {
            ChunkId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Vector = nanVector
        };

        var act = async () => await store.UpsertVectorsAsync(new[] { badPoint });
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*contains invalid float*");
    }

    [Fact]
    public async Task UpsertVectorsAsync_VectorContainsInfinity_ThrowsArgumentException()
    {
        var client = Substitute.For<IQdrantClient>();
        var store = new QdrantVectorStore(client, _optionsWrapper, NullLogger<QdrantVectorStore>.Instance);

        var infVector = new float[768];
        infVector[20] = float.PositiveInfinity;

        var badPoint = new VectorPoint
        {
            ChunkId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Vector = infVector
        };

        var act = async () => await store.UpsertVectorsAsync(new[] { badPoint });
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*contains invalid float*");
    }

    [Fact]
    public async Task UpsertVectorsAsync_ValidVectors_CallsClientUpsert()
    {
        var client = Substitute.For<IQdrantClient>();
        var store = new QdrantVectorStore(client, _optionsWrapper, NullLogger<QdrantVectorStore>.Instance);

        var pt = new VectorPoint
        {
            ChunkId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Vector = new float[768],
            Content = "Sample test text",
            FileName = "Test.md",
            RelativePath = "Faults/Test.md",
            Folder = "Faults",
            Tags = new List<string> { "tag1" }
        };

        await store.UpsertVectorsAsync(new[] { pt });

        await client.Received(1).UpsertAsync(
            "cell-knowledge",
            Arg.Is<IReadOnlyList<PointStruct>>(p => p.Count == 1),
            wait: true,
            ordering: null,
            shardKeySelector: null,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteByDocumentIdAsync_EmptyGuid_DoesNothing()
    {
        var client = Substitute.For<IQdrantClient>();
        var store = new QdrantVectorStore(client, _optionsWrapper, NullLogger<QdrantVectorStore>.Instance);

        await store.DeleteByDocumentIdAsync(Guid.Empty);

        await client.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default(Filter)!, default, default, default, default);
    }

    [Fact]
    public async Task DeleteByDocumentIdAsync_ValidGuid_CallsClientDelete()
    {
        var client = Substitute.For<IQdrantClient>();
        var store = new QdrantVectorStore(client, _optionsWrapper, NullLogger<QdrantVectorStore>.Instance);

        var docId = Guid.NewGuid();
        await store.DeleteByDocumentIdAsync(docId);

        await client.Received(1).DeleteAsync(
            "cell-knowledge",
            Arg.Any<Filter>(),
            wait: true,
            ordering: null,
            shardKeySelector: null,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetVectorCountAsync_ReturnsCountFromClient()
    {
        var client = Substitute.For<IQdrantClient>();
        client.CountAsync("cell-knowledge", null, exact: true, null, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((ulong)42));

        var store = new QdrantVectorStore(client, _optionsWrapper, NullLogger<QdrantVectorStore>.Instance);
        var count = await store.GetVectorCountAsync();

        count.Should().Be(42);
    }

    [Fact]
    public async Task SearchHybridAsync_MismatchedVectorDimension_ReturnsEmptyList()
    {
        var client = Substitute.For<IQdrantClient>();
        var store = new QdrantVectorStore(client, _optionsWrapper, NullLogger<QdrantVectorStore>.Instance);

        var result = await store.SearchHybridAsync(new float[100], null, null, 5);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchHybridAsync_ExactIdentifierMatch_BoostsRankingScore()
    {
        var client = Substitute.For<IQdrantClient>();
        var store = new QdrantVectorStore(client, _optionsWrapper, NullLogger<QdrantVectorStore>.Instance);

        var docId1 = Guid.NewGuid();
        var docId2 = Guid.NewGuid();

        var point1 = new ScoredPoint
        {
            Id = new PointId { Uuid = Guid.NewGuid().ToString() },
            Score = 0.82f,
            Payload =
            {
                ["documentId"] = docId1.ToString(),
                ["fileName"] = "GeneralRobot.md",
                ["relativePath"] = "Equipment/GeneralRobot.md",
                ["folder"] = "Equipment",
                ["content"] = "General robot operating principles."
            }
        };

        var point2 = new ScoredPoint
        {
            Id = new PointId { Uuid = Guid.NewGuid().ToString() },
            Score = 0.76f,
            Payload =
            {
                ["documentId"] = docId2.ToString(),
                ["fileName"] = "AlarmGuide.md",
                ["relativePath"] = "Alarms/AlarmGuide.md",
                ["folder"] = "Alarms",
                ["alarmId"] = "ALM-205",
                ["content"] = "ALM-205 triggered by safety gate sensor."
            }
        };

#pragma warning disable CS0618
        client.SearchAsync(
            collectionName: "cell-knowledge",
            vector: Arg.Any<ReadOnlyMemory<float>>(),
            filter: Arg.Any<Filter>(),
            searchParams: Arg.Any<SearchParams>(),
            limit: Arg.Any<ulong>(),
            offset: Arg.Any<ulong>(),
            payloadSelector: Arg.Any<WithPayloadSelector>(),
            vectorsSelector: Arg.Any<WithVectorsSelector>(),
            scoreThreshold: Arg.Any<float?>(),
            vectorName: Arg.Any<string>(),
            readConsistency: Arg.Any<ReadConsistency>(),
            shardKeySelector: Arg.Any<ShardKeySelector>(),
            sparseIndices: Arg.Any<ReadOnlyMemory<uint>?>(),
            timeout: Arg.Any<TimeSpan?>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScoredPoint>>(new List<ScoredPoint> { point1, point2 }));
#pragma warning restore CS0618

        var results = await store.SearchHybridAsync(
            queryVector: new float[768],
            exactKeywords: new[] { "ALM-205" },
            equipmentId: null,
            folder: null,
            limit: 5);

        results.Should().HaveCount(2);
        // Point2 should rank FIRST due to exact identifier match boost (+0.15: 0.76 + 0.15 = 0.91 vs 0.82)
        results[0].Source.FileName.Should().Be("AlarmGuide.md");
        results[0].ExactIdentifierMatch.Should().BeTrue();
        results[0].SimilarityScore.Should().BeApproximately(0.76, 0.001);

        results[1].Source.FileName.Should().Be("GeneralRobot.md");
        results[1].ExactIdentifierMatch.Should().BeFalse();
    }

    [Fact]
    public async Task SearchHybridAsync_BelowMinimumRelevanceThreshold_FilteredOut()
    {
        var client = Substitute.For<IQdrantClient>();
        var store = new QdrantVectorStore(client, _optionsWrapper, NullLogger<QdrantVectorStore>.Instance);

        var weakPoint = new ScoredPoint
        {
            Id = new PointId { Uuid = Guid.NewGuid().ToString() },
            Score = 0.35f,
            Payload =
            {
                ["documentId"] = Guid.NewGuid().ToString(),
                ["fileName"] = "Weak.md",
                ["relativePath"] = "Faults/Weak.md",
                ["folder"] = "Faults",
                ["content"] = "Irrelevant documentation."
            }
        };

#pragma warning disable CS0618
        client.SearchAsync(
            collectionName: "cell-knowledge",
            vector: Arg.Any<ReadOnlyMemory<float>>(),
            filter: Arg.Any<Filter>(),
            searchParams: Arg.Any<SearchParams>(),
            limit: Arg.Any<ulong>(),
            offset: Arg.Any<ulong>(),
            payloadSelector: Arg.Any<WithPayloadSelector>(),
            vectorsSelector: Arg.Any<WithVectorsSelector>(),
            scoreThreshold: Arg.Any<float?>(),
            vectorName: Arg.Any<string>(),
            readConsistency: Arg.Any<ReadConsistency>(),
            shardKeySelector: Arg.Any<ShardKeySelector>(),
            sparseIndices: Arg.Any<ReadOnlyMemory<uint>?>(),
            timeout: Arg.Any<TimeSpan?>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScoredPoint>>(new List<ScoredPoint> { weakPoint }));
#pragma warning restore CS0618

        var results = await store.SearchHybridAsync(new float[768], null, null, 5);
        results.Should().BeEmpty();
    }
}

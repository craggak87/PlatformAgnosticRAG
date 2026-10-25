namespace CellAssistant.UnitTests.Infrastructure.Persistence;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CellAssistant.Domain.Entities;
using CellAssistant.Domain.Enums;
using CellAssistant.Domain.ValueObjects;
using CellAssistant.Infrastructure.Persistence.Sqlite;
using FluentAssertions;
using Xunit;

public class SqliteSearchHistoryRepositoryTests : IDisposable
{
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteSearchHistoryRepository _repository;

    public SqliteSearchHistoryRepositoryTests()
    {
        _factory = new SqliteConnectionFactory(":memory:");
        var initializer = new SqliteDatabaseInitializer(_factory);
        initializer.Initialize();
        _repository = new SqliteSearchHistoryRepository(_factory);
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    [Fact]
    public async Task AddAsync_And_GetRecentAsync_OrdersByAskedUtcDesc()
    {
        var entry1 = new SearchHistoryEntry
        {
            Id = Guid.NewGuid(),
            Question = "Older question",
            Answer = "Older answer",
            Confidence = ConfidenceLevel.Medium,
            AskedUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        var entry2 = new SearchHistoryEntry
        {
            Id = Guid.NewGuid(),
            Question = "Newer question",
            Answer = "Newer answer",
            Confidence = ConfidenceLevel.High,
            AskedUtc = DateTimeOffset.UtcNow
        };

        await _repository.AddAsync(entry1);
        await _repository.AddAsync(entry2);

        var recent = await _repository.GetRecentAsync(10);
        recent.Should().HaveCount(2);
        recent[0].Id.Should().Be(entry2.Id); // Newer first
        recent[1].Id.Should().Be(entry1.Id);
    }

    [Fact]
    public async Task GetRecentAsync_ZeroOrNegativeLimit_DefaultsTo20()
    {
        for (int i = 0; i < 25; i++)
        {
            await _repository.AddAsync(new SearchHistoryEntry
            {
                Id = Guid.NewGuid(),
                Question = $"Question {i}",
                Answer = $"Answer {i}",
                Confidence = ConfidenceLevel.Low,
                AskedUtc = DateTimeOffset.UtcNow.AddSeconds(i)
            });
        }

        var resultsZero = await _repository.GetRecentAsync(0);
        resultsZero.Should().HaveCount(20);

        var resultsNegative = await _repository.GetRecentAsync(-5);
        resultsNegative.Should().HaveCount(20);
    }

    [Fact]
    public async Task GetRecentAsync_LimitAbove100_CappedAt100()
    {
        for (int i = 0; i < 110; i++)
        {
            await _repository.AddAsync(new SearchHistoryEntry
            {
                Id = Guid.NewGuid(),
                Question = $"Question {i}",
                Answer = $"Answer {i}",
                Confidence = ConfidenceLevel.High,
                AskedUtc = DateTimeOffset.UtcNow.AddSeconds(i)
            });
        }

        var results = await _repository.GetRecentAsync(500);
        results.Should().HaveCount(100);
    }

    [Fact]
    public async Task SourcesJson_RoundTrip_PreservesCitations()
    {
        var docId = Guid.NewGuid();
        var sources = new List<SourceReference>
        {
            new(docId, "Manual.pdf", "Manuals/Manual.pdf", "Manuals", PageNumber: 12, SectionHeading: null),
            new(Guid.NewGuid(), "Faults.md", "Faults/Faults.md", "Faults", PageNumber: null, SectionHeading: "Symptoms")
        };

        var entry = new SearchHistoryEntry
        {
            Id = Guid.NewGuid(),
            Question = "What causes fault FLT-001?",
            Answer = "Check pneumatic pressure.",
            Confidence = ConfidenceLevel.High,
            Sources = sources,
            AskedUtc = DateTimeOffset.UtcNow
        };

        await _repository.AddAsync(entry);

        var retrieved = await _repository.GetRecentAsync(1);
        retrieved.Should().HaveCount(1);
        var item = retrieved[0];
        item.Sources.Should().HaveCount(2);
        item.Sources[0].DocumentId.Should().Be(docId);
        item.Sources[0].FileName.Should().Be("Manual.pdf");
        item.Sources[0].PageNumber.Should().Be(12);
        item.Sources[1].SectionHeading.Should().Be("Symptoms");
    }

    [Fact]
    public async Task EmptyTable_ReturnsEmptyList()
    {
        var results = await _repository.GetRecentAsync(10);
        results.Should().BeEmpty();
    }
}

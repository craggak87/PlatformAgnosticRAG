namespace CellAssistant.UnitTests.Infrastructure.Persistence;

using System;
using System.Threading.Tasks;
using CellAssistant.Domain.Entities;
using CellAssistant.Domain.Enums;
using CellAssistant.Infrastructure.Persistence.Sqlite;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

public class SqliteIndexerStateRepositoryTests : IDisposable
{
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteIndexerStateRepository _repository;

    public SqliteIndexerStateRepositoryTests()
    {
        _factory = new SqliteConnectionFactory(":memory:");
        var initializer = new SqliteDatabaseInitializer(_factory);
        initializer.Initialize();
        _repository = new SqliteIndexerStateRepository(_factory);
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    [Fact]
    public async Task GetStateAsync_UnseededTable_ReturnsDefaultIdleState()
    {
        var state = await _repository.GetStateAsync();
        state.Should().NotBeNull();
        state.Id.Should().Be(1);
        state.Status.Should().Be(IndexerStatus.Idle);
        state.LastRunUtc.Should().BeNull();
        state.LastError.Should().BeNull();
        state.ReindexRequested.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateStateAsync_PersistsAllFields()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new IndexerState
        {
            Id = 1,
            Status = IndexerStatus.Indexing,
            LastRunUtc = now,
            LastError = "Transient socket warning",
            ReindexRequested = true
        };

        await _repository.UpdateStateAsync(state);

        var retrieved = await _repository.GetStateAsync();
        retrieved.Status.Should().Be(IndexerStatus.Indexing);
        retrieved.LastError.Should().Be("Transient socket warning");
        retrieved.ReindexRequested.Should().BeTrue();
        retrieved.LastRunUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateStatusAsync_UpdatesStatusAndError()
    {
        await _repository.UpdateStatusAsync(IndexerStatus.Failed, "Disk full");

        var state = await _repository.GetStateAsync();
        state.Status.Should().Be(IndexerStatus.Failed);
        state.LastError.Should().Be("Disk full");
    }

    [Fact]
    public async Task SetReindexRequestedAsync_SetsFlag()
    {
        await _repository.SetReindexRequestedAsync(true);
        var state1 = await _repository.GetStateAsync();
        state1.ReindexRequested.Should().BeTrue();

        await _repository.SetReindexRequestedAsync(false);
        var state2 = await _repository.GetStateAsync();
        state2.ReindexRequested.Should().BeFalse();
    }

    [Fact]
    public async Task InsertWithInvalidId_ThrowsSqliteException()
    {
        using var conn = await _factory.CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO indexer_state (id, status) VALUES (2, 'Idle');";

        var act = async () => await cmd.ExecuteNonQueryAsync();
        await act.Should().ThrowAsync<SqliteException>();
    }
}

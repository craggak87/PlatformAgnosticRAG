namespace CellAssistant.UnitTests.Infrastructure.Persistence;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CellAssistant.Domain.Entities;
using CellAssistant.Domain.Enums;
using CellAssistant.Infrastructure.Persistence.Sqlite;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

public class SqliteDocumentRepositoryTests : IDisposable
{
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteDocumentRepository _repository;

    public SqliteDocumentRepositoryTests()
    {
        _factory = new SqliteConnectionFactory(":memory:");
        var initializer = new SqliteDatabaseInitializer(_factory);
        initializer.Initialize();
        _repository = new SqliteDocumentRepository(_factory);
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    [Fact]
    public async Task UpsertAsync_NewDocument_InsertsRecord()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            FileName = "Robot Gripper.md",
            FilePath = "/kb/Faults/Robot Gripper.md",
            RelativePath = "Faults/Robot Gripper.md",
            DocumentType = DocumentType.Markdown,
            Folder = "Faults",
            ContentHash = "abcdef0123456789",
            EquipmentId = "ROBOT01",
            Manufacturer = "Fanuc",
            Criticality = "High",
            Tags = new List<string> { "robot", "gripper", "pneumatics" },
            ChunkCount = 4,
            CreatedUtc = DateTimeOffset.UtcNow,
            ModifiedUtc = DateTimeOffset.UtcNow,
            IndexedUtc = DateTimeOffset.UtcNow
        };

        await _repository.UpsertAsync(doc);

        var retrieved = await _repository.GetByIdAsync(doc.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(doc.Id);
        retrieved.FileName.Should().Be("Robot Gripper.md");
        retrieved.FilePath.Should().Be("/kb/Faults/Robot Gripper.md");
        retrieved.RelativePath.Should().Be("Faults/Robot Gripper.md");
        retrieved.DocumentType.Should().Be(DocumentType.Markdown);
        retrieved.Folder.Should().Be("Faults");
        retrieved.ContentHash.Should().Be("abcdef0123456789");
        retrieved.EquipmentId.Should().Be("ROBOT01");
        retrieved.Manufacturer.Should().Be("Fanuc");
        retrieved.Criticality.Should().Be("High");
        retrieved.Tags.Should().ContainInOrder("robot", "gripper", "pneumatics");
        retrieved.ChunkCount.Should().Be(4);
    }

    [Fact]
    public async Task UpsertAsync_ExistingId_UpdatesRecord()
    {
        var docId = Guid.NewGuid();
        var doc = new Document
        {
            Id = docId,
            FileName = "VFD.md",
            FilePath = "/kb/Electrical/VFD.md",
            RelativePath = "Electrical/VFD.md",
            DocumentType = DocumentType.Markdown,
            Folder = "Electrical",
            ContentHash = "hash1",
            ChunkCount = 1
        };

        await _repository.UpsertAsync(doc);

        // Update properties
        doc.ContentHash = "hash2";
        doc.ChunkCount = 5;
        doc.EquipmentId = "VFD01";
        await _repository.UpsertAsync(doc);

        var updated = await _repository.GetByIdAsync(docId);
        updated.Should().NotBeNull();
        updated!.ContentHash.Should().Be("hash2");
        updated.ChunkCount.Should().Be(5);
        updated.EquipmentId.Should().Be("VFD01");
    }

    [Fact]
    public async Task UpsertAsync_DuplicateFilePathWithDifferentId_ThrowsSqliteException()
    {
        var doc1 = new Document
        {
            Id = Guid.NewGuid(),
            FileName = "Doc.md",
            FilePath = "/kb/Common/Doc.md",
            RelativePath = "Common/Doc.md",
            DocumentType = DocumentType.Markdown,
            Folder = "Common",
            ContentHash = "h1"
        };
        await _repository.UpsertAsync(doc1);

        var doc2 = new Document
        {
            Id = Guid.NewGuid(), // Different ID
            FileName = "Doc.md",
            FilePath = "/kb/Common/Doc.md", // Same file path
            RelativePath = "Common/Doc.md",
            DocumentType = DocumentType.Markdown,
            Folder = "Common",
            ContentHash = "h2"
        };

        var act = async () => await _repository.UpsertAsync(doc2);
        await act.Should().ThrowAsync<SqliteException>();
    }

    [Fact]
    public async Task GetByFilePathAsync_ExistingPath_ReturnsDocument()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            FileName = "Safety.md",
            FilePath = "/kb/Safety/Safety.md",
            RelativePath = "Safety/Safety.md",
            DocumentType = DocumentType.Markdown,
            Folder = "Safety",
            ContentHash = "shash"
        };
        await _repository.UpsertAsync(doc);

        var retrieved = await _repository.GetByFilePathAsync("/kb/Safety/Safety.md");
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(doc.Id);
    }

    [Fact]
    public async Task GetByFilePathAsync_NonExistentPath_ReturnsNull()
    {
        var retrieved = await _repository.GetByFilePathAsync("/kb/NonExistent/File.md");
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_MultipleDocuments_ReturnsAll()
    {
        var doc1 = new Document
        {
            Id = Guid.NewGuid(),
            FileName = "Doc1.md",
            FilePath = "/kb/Folder/Doc1.md",
            RelativePath = "Folder/Doc1.md",
            Folder = "Folder",
            ContentHash = "h1"
        };
        var doc2 = new Document
        {
            Id = Guid.NewGuid(),
            FileName = "Doc2.md",
            FilePath = "/kb/Folder/Doc2.md",
            RelativePath = "Folder/Doc2.md",
            Folder = "Folder",
            ContentHash = "h2"
        };

        await _repository.UpsertAsync(doc1);
        await _repository.UpsertAsync(doc2);

        var all = await _repository.GetAllAsync();
        all.Should().HaveCount(2);
        all.Should().Contain(d => d.Id == doc1.Id);
        all.Should().Contain(d => d.Id == doc2.Id);
    }

    [Fact]
    public async Task DeleteByIdAsync_RemovesDocument()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            FileName = "DeleteMe.md",
            FilePath = "/kb/DeleteMe.md",
            RelativePath = "DeleteMe.md",
            Folder = "Root",
            ContentHash = "h"
        };
        await _repository.UpsertAsync(doc);

        var initialCount = await _repository.GetCountAsync();
        initialCount.Should().Be(1);

        await _repository.DeleteByIdAsync(doc.Id);

        var postDelete = await _repository.GetByIdAsync(doc.Id);
        postDelete.Should().BeNull();

        var postCount = await _repository.GetCountAsync();
        postCount.Should().Be(0);
    }

    [Fact]
    public async Task NullableProperties_StoredAndRetrievedAsNull()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            FileName = "NullProps.md",
            FilePath = "/kb/NullProps.md",
            RelativePath = "NullProps.md",
            Folder = "Root",
            ContentHash = "h",
            EquipmentId = null,
            Manufacturer = null,
            Criticality = null
        };
        await _repository.UpsertAsync(doc);

        var retrieved = await _repository.GetByIdAsync(doc.Id);
        retrieved.Should().NotBeNull();
        retrieved!.EquipmentId.Should().BeNull();
        retrieved.Manufacturer.Should().BeNull();
        retrieved.Criticality.Should().BeNull();
    }
}

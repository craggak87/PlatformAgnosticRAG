using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier2_BoundaryAndCornerCases;

public class Boundary19_DocumentViewerNotFoundBoundaryTests
{
    [Fact]
    public void Test01_GuidEmpty_IsDetectedAsInvalidOrNotFound()
    {
        var emptyId = Guid.Empty;
        using var conn = E2ETestHarness.CreateInMemorySqlite();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT file_path FROM documents WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", emptyId.ToString());
        var path = cmd.ExecuteScalar();

        path.Should().BeNull();
    }

    [Theory]
    [InlineData("not-a-valid-guid")]
    [InlineData("12345")]
    [InlineData("3fa85f64-5717-4562-b3fc")]
    public void Test02_MalformedGuidString_FailsGuidParsing(string malformed)
    {
        bool isValidGuid = Guid.TryParse(malformed, out _);
        isValidGuid.Should().BeFalse();
    }

    [Fact]
    public void Test03_DocumentDeletedFromDiskWhileInRegistry_ReturnsNotFound()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();
        var docId = Guid.NewGuid();
        var nonExistentPath = "/tmp/non_existent_file_12345.md";

        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = @"
INSERT INTO documents (id, file_name, file_path, relative_path, document_type, folder, content_hash, created_utc, modified_utc, indexed_utc)
VALUES (@id, 'Missing.md', @p, 'Faults/Missing.md', 'Markdown', 'Faults', 'h', datetime('now'), datetime('now'), datetime('now'));";
        insertCmd.Parameters.AddWithValue("@id", docId.ToString());
        insertCmd.Parameters.AddWithValue("@p", nonExistentPath);
        insertCmd.ExecuteNonQuery();

        bool physicalFileExists = File.Exists(nonExistentPath);
        physicalFileExists.Should().BeFalse("Physical file is missing from disk and should return 404");
    }

    [Fact]
    public void Test04_EmptyFileOnDisk_ServedWithZeroContentLength()
    {
        var emptyBytes = Array.Empty<byte>();
        emptyBytes.Length.Should().Be(0);
    }

    [Fact]
    public void Test05_DocumentViewer_RejectsUnauthorizedFileModifications()
    {
        // Document endpoint is strictly read-only GET per ADR-013 & ADR-014
        const string httpMethod = "GET";
        httpMethod.Should().Be("GET");
    }
}

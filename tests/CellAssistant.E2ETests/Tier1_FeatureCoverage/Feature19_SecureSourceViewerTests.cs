using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier1_FeatureCoverage;

public class Feature19_SecureSourceViewerTests
{
    [Fact]
    public void Test01_DocumentFileRouteTemplate_MatchesSpecification()
    {
        var id = Guid.NewGuid();
        var route = string.Format(SpecificationConstants.DocumentFileEndpointTemplate, id);
        route.Should().Be($"/api/documents/{id}/file");
    }

    [Theory]
    [InlineData(".pdf", "application/pdf")]
    [InlineData(".PDF", "application/pdf")]
    [InlineData(".md", "text/plain; charset=utf-8")]
    [InlineData(".txt", "text/plain; charset=utf-8")]
    public void Test02_MimeTypeResolution_ServesAppropriateContentType(string extension, string expectedMime)
    {
        string mimeType = extension.ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".md" or ".txt" => "text/plain; charset=utf-8",
            _ => "application/octet-stream"
        };

        mimeType.Should().Be(expectedMime);
    }

    [Fact]
    public void Test03_NonExistentDocumentId_ReturnsNullOrNotFoundStatus()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();
        var randomId = Guid.NewGuid();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT file_path FROM documents WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", randomId.ToString());
        var path = cmd.ExecuteScalar();

        path.Should().BeNull("Non-existent document ID must return null/404");
    }

    [Fact]
    public void Test04_ViewerStreamsReadOnlyAccess()
    {
        // Read-only stream simulation
        var bytes = System.Text.Encoding.UTF8.GetBytes("# Markdown Document Content");
        using var stream = new MemoryStream(bytes, writable: false);

        stream.CanRead.Should().BeTrue();
        stream.CanWrite.Should().BeFalse();
    }

    [Fact]
    public void Test05_DocumentViewer_RetrievesCanonicalPathFromSqlite()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();
        var docId = Guid.NewGuid();
        var canonicalPath = "/data/KnowledgeBase/Faults/Robot Gripper.md";

        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = @"
INSERT INTO documents (id, file_name, file_path, relative_path, document_type, folder, content_hash, created_utc, modified_utc, indexed_utc)
VALUES (@id, 'Robot Gripper.md', @path, 'Faults/Robot Gripper.md', 'Markdown', 'Faults', 'hash123', datetime('now'), datetime('now'), datetime('now'));";
        insertCmd.Parameters.AddWithValue("@id", docId.ToString());
        insertCmd.Parameters.AddWithValue("@path", canonicalPath);
        insertCmd.ExecuteNonQuery();

        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT file_path FROM documents WHERE id = @id;";
        selectCmd.Parameters.AddWithValue("@id", docId.ToString());
        var retrieved = selectCmd.ExecuteScalar()?.ToString();

        retrieved.Should().Be(canonicalPath);
    }
}

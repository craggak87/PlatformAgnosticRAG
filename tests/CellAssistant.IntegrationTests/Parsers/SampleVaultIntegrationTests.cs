namespace CellAssistant.IntegrationTests.Parsers;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CellAssistant.Domain.Enums;
using CellAssistant.Infrastructure.Parsing;
using FluentAssertions;
using Xunit;

public class SampleVaultIntegrationTests
{
    private static readonly string VaultRoot = ResolveSampleVaultPath();

    private static string ResolveSampleVaultPath()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, "sample-data", "KnowledgeBase");
            if (Directory.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate sample-data/KnowledgeBase from test directory.");
    }

    [Fact]
    public async Task ParseAll21MarkdownNotes_ExtractsFrontmatter_SplitsHeadings_EnrichesHeaders()
    {
        // Arrange
        var parser = new MarkdigYamlParser();
        var mdFiles = Directory.GetFiles(VaultRoot, "*.md", SearchOption.AllDirectories)
            .OrderBy(f => f)
            .ToList();

        // Assert: Exactly 21 markdown files exist in sample-data
        mdFiles.Should().HaveCount(21, "the sample-data knowledge base must contain exactly 21 Markdown files across 16 folders");

        int totalChunks = 0;
        int filesWithEquipment = 0;
        int filesWithTags = 0;

        foreach (var fullPath in mdFiles)
        {
            var relativePath = Path.GetRelativePath(VaultRoot, fullPath).Replace('\\', '/');
            var fileName = Path.GetFileName(fullPath);

            // Act
            var (doc, chunks) = await parser.ParseAsync(fullPath, relativePath);

            // Assert: Document Entity Properties
            doc.Should().NotBeNull();
            doc.Id.Should().NotBeEmpty();
            doc.FileName.Should().Be(fileName);
            doc.RelativePath.Should().Be(relativePath);
            doc.DocumentType.Should().Be(DocumentType.Markdown);
            doc.Folder.Should().NotBeNullOrWhiteSpace();
            doc.ContentHash.Should().HaveLength(64, "SHA-256 hex string is 64 characters");
            doc.ChunkCount.Should().Be(chunks.Count);
            chunks.Should().NotBeEmpty($"file {relativePath} should have at least 1 chunk");

            if (!string.IsNullOrEmpty(doc.EquipmentId))
                filesWithEquipment++;

            if (doc.Tags.Count > 0)
                filesWithTags++;

            // Assert: Chunk Properties & Header Enrichment
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                chunk.DocumentId.Should().Be(doc.Id);
                chunk.ChunkIndex.Should().Be(i);
                chunk.PageNumber.Should().BeNull("Markdown documents do not have page numbers; they use section headings");

                // Header enrichment assertion
                chunk.Content.Should().StartWith("[Document: ", $"chunk in {relativePath} must begin with standard context header");
                chunk.Content.Should().Contain($"| Folder: {doc.Folder}");

                if (!string.IsNullOrEmpty(doc.EquipmentId))
                {
                    chunk.Content.Should().Contain($"| Equipment: {doc.EquipmentId}");
                    chunk.EquipmentId.Should().Be(doc.EquipmentId);
                }

                if (!string.IsNullOrEmpty(chunk.SectionHeading))
                {
                    chunk.Content.Should().Contain($"| Page/Section: {chunk.SectionHeading}");
                }
            }

            totalChunks += chunks.Count;
        }

        // Vault-wide assertions
        filesWithEquipment.Should().BeGreaterThan(10, "majority of cell operational notes have equipment_id in frontmatter");
        filesWithTags.Should().BeGreaterThan(15, "majority of notes have frontmatter or inline tags");
        totalChunks.Should().BeGreaterThanOrEqualTo(21);
    }

    [Fact]
    public async Task ParseAll5BinaryPdfManuals_ExtractsMultiPageText_1BasedNumbering_EnrichesHeaders()
    {
        // Arrange
        var parser = new PdfPigParser();
        var manualsDir = Path.Combine(VaultRoot, "Manuals");
        Directory.Exists(manualsDir).Should().BeTrue();

        var pdfFiles = Directory.GetFiles(manualsDir, "*.pdf", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f)
            .ToList();

        // Assert: Exactly 5 PDF manuals in sample-data
        pdfFiles.Should().HaveCount(5, "the sample-data Manuals folder must contain 5 binary PDF manuals");

        var expectedPdfs = new[]
        {
            "PLC01_Siemens_S7-1500_Manual.pdf",
            "Pneumatics_SMC_SystemManual.pdf",
            "ROBOT01_ABB_IRB4600_Manual.pdf",
            "Safety_Interlock_Pilz_Manual.pdf",
            "VFD01_PowerFlex525_UserManual.pdf"
        };

        pdfFiles.Select(Path.GetFileName).Should().BeEquivalentTo(expectedPdfs);

        int totalPdfChunks = 0;

        foreach (var fullPath in pdfFiles)
        {
            var relativePath = Path.GetRelativePath(VaultRoot, fullPath).Replace('\\', '/');
            var fileName = Path.GetFileName(fullPath);

            // Act
            var (doc, chunks) = await parser.ParseAsync(fullPath, relativePath);

            // Assert: Document Entity Properties
            doc.Should().NotBeNull();
            doc.Id.Should().NotBeEmpty();
            doc.FileName.Should().Be(fileName);
            doc.RelativePath.Should().Be(relativePath);
            doc.DocumentType.Should().Be(DocumentType.Pdf);
            doc.Folder.Should().Be("Manuals");
            doc.ContentHash.Should().HaveLength(64);
            doc.ChunkCount.Should().Be(chunks.Count);
            doc.Tags.Should().Contain("manual");
            chunks.Should().NotBeEmpty($"PDF manual {fileName} must produce text chunks");

            // Verify inferred equipment ID from file name
            if (fileName.Contains("ROBOT01"))
                doc.EquipmentId.Should().Be("ROBOT01");
            else if (fileName.Contains("PLC01"))
                doc.EquipmentId.Should().Be("PLC01");
            else if (fileName.Contains("VFD01"))
                doc.EquipmentId.Should().Be("VFD01");

            // Assert: 1-based page numbering and chunk header enrichment
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                chunk.DocumentId.Should().Be(doc.Id);
                chunk.ChunkIndex.Should().Be(i);
                chunk.PageNumber.Should().NotBeNull("PDF chunks must have a page number");
                chunk.PageNumber.Should().BeGreaterThanOrEqualTo(1, "page numbering in PDF manuals must be 1-based");
                chunk.SectionHeading.Should().BeNull("PDF chunks use page numbers rather than Markdown headings");
                chunk.Tags.Should().Contain("manual");

                // Header enrichment assertion
                chunk.Content.Should().StartWith("[Document: ", $"PDF chunk {i} in {fileName} must begin with context header");
                chunk.Content.Should().Contain("| Folder: Manuals");
                chunk.Content.Should().Contain($"| Page/Section: Page {chunk.PageNumber}");
            }

            // Verify multi-page presence
            var distinctPages = chunks.Select(c => c.PageNumber!.Value).Distinct().ToList();
            distinctPages.Count.Should().BeGreaterThan(1, $"PDF manual {fileName} should be multi-page");

            totalPdfChunks += chunks.Count;
        }

        totalPdfChunks.Should().BeGreaterThan(10);
    }
}

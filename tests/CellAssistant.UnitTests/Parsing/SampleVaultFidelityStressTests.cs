namespace CellAssistant.UnitTests.Parsing;

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CellAssistant.Domain.Enums;
using CellAssistant.Infrastructure.Parsing;
using FluentAssertions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Xunit;

public class SampleVaultFidelityStressTests
{
    private readonly string _vaultRoot;
    private readonly MarkdigYamlParser _markdownParser;
    private readonly PdfPigParser _pdfParser;

    public SampleVaultFidelityStressTests()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current) && !File.Exists(Path.Combine(current, "1.Cell_Knowledge_Assistant_ADR.md")))
        {
            current = Directory.GetParent(current)?.FullName;
        }
        var repoRoot = current ?? Directory.GetCurrentDirectory();
        _vaultRoot = Path.Combine(repoRoot, "sample-data", "KnowledgeBase");

        _markdownParser = new MarkdigYamlParser();
        _pdfParser = new PdfPigParser();
    }

    [Fact]
    public void VaultRoot_MustExistAndContainAll16StandardFolders()
    {
        Directory.Exists(_vaultRoot).Should().BeTrue($"Vault root must exist at '{_vaultRoot}'");

        var expectedFolders = new[]
        {
            "Equipment", "Faults", "PLC", "Robot", "Vision", "Safety",
            "Electrical", "Pneumatics", "Mechanical", "Maintenance", "Alarms",
            "Procedures", "Improvements", "Lessons-Learned", "Drawings", "Manuals"
        };

        foreach (var folder in expectedFolders)
        {
            var folderPath = Path.Combine(_vaultRoot, folder);
            Directory.Exists(folderPath).Should().BeTrue($"Standard folder '{folder}' must exist");
        }
    }

    [Fact]
    public async Task MarkdigYamlParser_AllSampleVaultNotes_ParseFrontmatterAndChunksCleanly()
    {
        var mdFiles = Directory.GetFiles(_vaultRoot, "*.md", SearchOption.AllDirectories);
        mdFiles.Length.Should().BeGreaterOrEqualTo(21, "Sample vault must contain at least 21 markdown notes");

        foreach (var mdFile in mdFiles)
        {
            var relPath = Path.GetRelativePath(_vaultRoot, mdFile).Replace('\\', '/');

            // 1. CanParse check
            _markdownParser.CanParse(mdFile).Should().BeTrue($"MarkdigYamlParser must support '{mdFile}'");

            // 2. Parse without exceptions
            var (document, chunks) = await _markdownParser.ParseAsync(mdFile, relPath);

            // 3. Document entity integrity
            document.Should().NotBeNull();
            document.Id.Should().NotBeEmpty();
            document.FileName.Should().Be(Path.GetFileName(mdFile));
            document.DocumentType.Should().Be(DocumentType.Markdown);
            document.RelativePath.Should().Be(relPath);
            document.ContentHash.Should().MatchRegex("^[a-f0-9]{64}$", $"Valid SHA-256 hash required for {relPath}");
            document.ChunkCount.Should().Be(chunks.Count);
            chunks.Count.Should().BeGreaterThan(0, $"File '{relPath}' must generate at least 1 chunk");

            // 4. Frontmatter extraction verification
            var rawText = await File.ReadAllTextAsync(mdFile);
            if (rawText.StartsWith("---"))
            {
                var endYaml = rawText.IndexOf("---", 3, StringComparison.Ordinal);
                if (endYaml > 3)
                {
                    var yamlSegment = rawText.Substring(3, endYaml - 3);

                    if (yamlSegment.Contains("equipment_id:"))
                    {
                        document.EquipmentId.Should().NotBeNullOrWhiteSpace($"EquipmentId expected from frontmatter in {relPath}");
                    }
                    if (yamlSegment.Contains("manufacturer:"))
                    {
                        document.Manufacturer.Should().NotBeNullOrWhiteSpace($"Manufacturer expected from frontmatter in {relPath}");
                    }
                    if (yamlSegment.Contains("criticality:"))
                    {
                        document.Criticality.Should().NotBeNullOrWhiteSpace($"Criticality expected from frontmatter in {relPath}");
                    }
                }
            }

            // 5. Chunk header generation verification
            var headerRegex = new Regex(@"^\[Document:\s*([^|]+)\|\s*Folder:\s*([^|]+)\|\s*Equipment:\s*([^|]*)\|\s*Fault/Alarm:\s*([^|]*)\|\s*Page/Section:\s*([^\]]+)\]");

            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                chunk.DocumentId.Should().Be(document.Id);
                chunk.ChunkIndex.Should().Be(i);
                chunk.Content.Should().NotBeNullOrWhiteSpace();

                // Chunk header check
                var match = headerRegex.Match(chunk.Content);
                match.Success.Should().BeTrue($"Chunk content must begin with context header in '{relPath}' chunk {i}. Actual: {chunk.Content.Substring(0, Math.Min(100, chunk.Content.Length))}");

                // Document name in header matches
                match.Groups[1].Value.Trim().Should().Be(document.FileName);

                // Section heading consistency
                if (!string.IsNullOrEmpty(chunk.SectionHeading))
                {
                    match.Groups[5].Value.Trim().Should().Be(chunk.SectionHeading);
                }
            }
        }
    }

    [Fact]
    public async Task PdfPigParser_AllSampleVaultPdfs_ParseCleanlyAndExtractAllPages()
    {
        var pdfFiles = Directory.GetFiles(_vaultRoot, "*.pdf", SearchOption.AllDirectories);
        pdfFiles.Length.Should().BeGreaterOrEqualTo(5, "Sample vault must contain at least 5 PDF manuals");

        foreach (var pdfFile in pdfFiles)
        {
            var relPath = Path.GetRelativePath(_vaultRoot, pdfFile).Replace('\\', '/');

            // 1. CanParse check
            _pdfParser.CanParse(pdfFile).Should().BeTrue($"PdfPigParser must support '{pdfFile}'");

            // 2. Direct UglyToad.PdfPig low-level inspection
            var rawBytes = await File.ReadAllBytesAsync(pdfFile);
            using (var pdfDoc = PdfDocument.Open(rawBytes))
            {
                pdfDoc.NumberOfPages.Should().BeGreaterThan(1, $"PDF '{relPath}' must be multi-page (> 1)");

                for (int pageNum = 1; pageNum <= pdfDoc.NumberOfPages; pageNum++)
                {
                    var page = pdfDoc.GetPage(pageNum);
                    page.Number.Should().Be(pageNum, "PdfPig page numbers must be 1-based");

                    var text = ContentOrderTextExtractor.GetText(page);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        text = page.Text;
                    }

                    text.Should().NotBeNullOrWhiteSpace($"Page {pageNum} of PDF '{relPath}' must yield readable extracted text");
                }
            }

            // 3. Application PdfPigParser execution
            var (document, chunks) = await _pdfParser.ParseAsync(pdfFile, relPath);

            document.Should().NotBeNull();
            document.DocumentType.Should().Be(DocumentType.Pdf);
            document.FileName.Should().Be(Path.GetFileName(pdfFile));
            document.RelativePath.Should().Be(relPath);
            document.Folder.Should().Be("Manuals");
            document.ContentHash.Should().MatchRegex("^[a-f0-9]{64}$");
            document.ChunkCount.Should().Be(chunks.Count);
            chunks.Count.Should().BeGreaterThan(1, $"PDF '{relPath}' must generate at least 2 chunks across its pages");

            // 4. Verify 1-based page numbers and header generation in chunks
            var distinctPages = chunks.Select(c => c.PageNumber).Distinct().ToList();
            distinctPages.Count.Should().BeGreaterThan(1, $"PDF '{relPath}' chunks must span multiple distinct pages");

            foreach (var chunk in chunks)
            {
                chunk.PageNumber.Should().NotBeNull($"PDF chunks must preserve 1-based PageNumber in '{relPath}'");
                chunk.PageNumber!.Value.Should().BeGreaterThanOrEqualTo(1, "Page numbers must be 1-based");

                // Check header
                chunk.Content.Should().StartWith("[Document: ");
                chunk.Content.Should().Contain($"Page/Section: Page {chunk.PageNumber.Value}");
            }
        }
    }
}

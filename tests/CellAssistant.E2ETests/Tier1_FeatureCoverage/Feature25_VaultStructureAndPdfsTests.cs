using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier1_FeatureCoverage;

public class Feature25_VaultStructureAndPdfsTests
{
    private readonly string _vaultRoot;

    public Feature25_VaultStructureAndPdfsTests()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current) && !File.Exists(Path.Combine(current, "1.Cell_Knowledge_Assistant_ADR.md")))
        {
            current = Directory.GetParent(current)?.FullName;
        }
        var repoRoot = current ?? Directory.GetCurrentDirectory();
        _vaultRoot = Path.Combine(repoRoot, "sample-data", "KnowledgeBase");
    }

    [Fact]
    public void Test01_All16StandardFolders_AreExpectedPerSpecification()
    {
        SpecificationConstants.StandardVaultFolders.Length.Should().Be(16);
        SpecificationConstants.StandardVaultFolders.Should().Contain(new[]
        {
            "Equipment", "Faults", "PLC", "Robot", "Vision", "Safety",
            "Electrical", "Pneumatics", "Mechanical", "Maintenance", "Alarms",
            "Procedures", "Improvements", "Lessons-Learned", "Drawings", "Manuals"
        });
    }

    [Fact]
    public void Test02_SampleVault_WhenPresent_ContainsAll16Folders()
    {
        if (Directory.Exists(_vaultRoot) && Directory.EnumerateFileSystemEntries(_vaultRoot).Any())
        {
            foreach (var folder in SpecificationConstants.StandardVaultFolders)
            {
                var folderPath = Path.Combine(_vaultRoot, folder);
                Directory.Exists(folderPath).Should().BeTrue($"Expected vault folder '{folderPath}' to exist");
            }
        }
    }

    [Fact]
    public void Test03_SampleVault_WhenPresent_ContainsAtLeast20MarkdownNotes()
    {
        if (Directory.Exists(_vaultRoot) && Directory.EnumerateFileSystemEntries(_vaultRoot).Any())
        {
            var mdFiles = Directory.GetFiles(_vaultRoot, "*.md", SearchOption.AllDirectories);
            mdFiles.Length.Should().BeGreaterOrEqualTo(20, "Sample vault must contain at least 20 markdown notes per §R6");
        }
    }

    [Fact]
    public void Test04_SampleVault_WhenPresent_ContainsAtLeast5BinaryPdfs()
    {
        if (Directory.Exists(_vaultRoot) && Directory.EnumerateFileSystemEntries(_vaultRoot).Any())
        {
            var pdfFiles = Directory.GetFiles(_vaultRoot, "*.pdf", SearchOption.AllDirectories);
            pdfFiles.Length.Should().BeGreaterOrEqualTo(5, "Sample vault must contain at least 5 PDF manuals per §R6");

            // Verify binary PDF header magic bytes %PDF
            foreach (var pdf in pdfFiles)
            {
                using var fs = File.OpenRead(pdf);
                var header = new byte[4];
                fs.ReadExactly(header, 0, 4);
                var magic = System.Text.Encoding.ASCII.GetString(header);
                magic.Should().Be("%PDF", $"File '{pdf}' must be a valid binary PDF");
            }
        }
    }

    [Fact]
    public void Test05_SampleVault_MarkdownNotesHaveValidYamlFrontmatter()
    {
        if (Directory.Exists(_vaultRoot) && Directory.EnumerateFileSystemEntries(_vaultRoot).Any())
        {
            var mdFiles = Directory.GetFiles(_vaultRoot, "*.md", SearchOption.AllDirectories);
            foreach (var md in mdFiles)
            {
                var content = File.ReadAllText(md);
                if (content.StartsWith("---"))
                {
                    content.IndexOf("---", 3, StringComparison.Ordinal).Should().BeGreaterThan(3,
                        $"File '{md}' has an unclosed YAML frontmatter block");
                }
            }
        }
    }
}

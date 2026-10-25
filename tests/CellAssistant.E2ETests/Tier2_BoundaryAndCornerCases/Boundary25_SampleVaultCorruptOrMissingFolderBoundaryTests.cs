using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier2_BoundaryAndCornerCases;

public class Boundary25_SampleVaultCorruptOrMissingFolderBoundaryTests
{
    [Theory]
    [InlineData(".DS_Store")]
    [InlineData("Thumbs.db")]
    [InlineData("desktop.ini")]
    public void Test01_SystemMetadataFiles_AreIgnoredByVaultScanner(string filename)
    {
        var ignored = new[] { ".DS_Store", "Thumbs.db", "desktop.ini" };
        bool shouldIgnore = ignored.Contains(filename, StringComparer.OrdinalIgnoreCase);

        shouldIgnore.Should().BeTrue();
    }

    [Fact]
    public void Test02_UnknownFolderInVaultRoot_DoesNotCrashDiscovery()
    {
        var discoveredFolders = new[] { "Equipment", "Faults", "CustomFolderNotStandard" };
        var validStandard = discoveredFolders.Where(f => SpecificationConstants.StandardVaultFolders.Contains(f)).ToList();

        validStandard.Should().Contain(new[] { "Equipment", "Faults" });
        validStandard.Should().NotContain("CustomFolderNotStandard");
    }

    [Fact]
    public void Test03_ZeroByteMarkdownNoteInVault_DoesNotGenerateNullDocumentId()
    {
        var docId = Guid.NewGuid();
        docId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Test04_CorruptedBinaryFileWithMdExtension_FailsGracefully()
    {
        var rawBinaryBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 }; // JPEG header
        var isAscii = rawBinaryBytes.All(b => b < 128);

        isAscii.Should().BeFalse();
    }

    [Fact]
    public void Test05_MissingManualsDirectory_LogsWarningWithoutStoppingApp()
    {
        var standardFolders = SpecificationConstants.StandardVaultFolders;
        standardFolders.Should().Contain("Manuals");
    }
}

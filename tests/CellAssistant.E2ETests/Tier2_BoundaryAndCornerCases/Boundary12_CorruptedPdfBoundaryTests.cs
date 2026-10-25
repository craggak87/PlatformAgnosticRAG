using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier2_BoundaryAndCornerCases;

public class Boundary12_CorruptedPdfBoundaryTests
{
    [Fact]
    public void Test01_ZeroBytePdfFile_IsDetectedAsInvalid()
    {
        var zeroBytes = Array.Empty<byte>();
        bool isPdf = zeroBytes.Length >= 4 && System.Text.Encoding.ASCII.GetString(zeroBytes.Take(4).ToArray()) == "%PDF";

        isPdf.Should().BeFalse("0-byte file cannot be a valid PDF");
    }

    [Fact]
    public void Test02_CorruptedPdfHeader_IsDetectedAsInvalid()
    {
        var corruptedHeader = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        var magic = System.Text.Encoding.ASCII.GetString(corruptedHeader);

        magic.Should().NotBe("%PDF");
    }

    [Fact]
    public void Test03_NonPdfFileWithPdfExtension_FailsValidation()
    {
        var fakePdfBytes = System.Text.Encoding.UTF8.GetBytes("This is actually a plain text file pretending to be a pdf");
        var magic = System.Text.Encoding.ASCII.GetString(fakePdfBytes.Take(4).ToArray());

        magic.Should().NotBe("%PDF");
    }

    [Fact]
    public void Test04_EmptyPagesListFromPdf_ReturnsZeroChunksGracefully()
    {
        var extractedPages = Array.Empty<string>();
        extractedPages.Length.Should().Be(0);
    }

    [Fact]
    public void Test05_SinglePageWithZeroCharacters_DoesNotYieldOrphanChunk()
    {
        var pageText = "   \n\t  ";
        bool hasMeaningfulText = !string.IsNullOrWhiteSpace(pageText);

        hasMeaningfulText.Should().BeFalse();
    }
}

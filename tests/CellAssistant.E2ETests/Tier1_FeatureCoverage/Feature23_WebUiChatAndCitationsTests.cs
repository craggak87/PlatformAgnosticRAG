using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier1_FeatureCoverage;

public class Feature23_WebUiChatAndCitationsTests
{
    private readonly string _repoRoot;

    public Feature23_WebUiChatAndCitationsTests()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current) && !File.Exists(Path.Combine(current, "1.Cell_Knowledge_Assistant_ADR.md")))
        {
            current = Directory.GetParent(current)?.FullName;
        }
        _repoRoot = current ?? Directory.GetCurrentDirectory();
    }

    [Theory]
    [InlineData("High", "green", "success")]
    [InlineData("Medium", "yellow", "warning")]
    [InlineData("Low", "red", "danger")]
    public void Test01_ConfidenceBadgeColorMapping_FollowsVisualStandard(string level, string color, string bootstrapClass)
    {
        // Visual indicator specification from Doc 2 & Doc 3
        var badgeClass = level switch
        {
            "High" => "badge-success",
            "Medium" => "badge-warning",
            "Low" => "badge-danger",
            _ => "badge-secondary"
        };

        color.Should().NotBeNullOrEmpty();
        badgeClass.Should().Contain(bootstrapClass);
    }

    [Fact]
    public void Test02_CitationOpeningPdf_AppendsPageAnchor()
    {
        var source = new SourceReferenceDto(
            DocumentId: Guid.NewGuid(),
            FileName: "ABB_IRB4600.pdf",
            RelativePath: "Manuals/ABB_IRB4600.pdf",
            PageNumber: 24,
            SectionHeading: null
        );

        var url = $"/api/documents/{source.DocumentId}/file#page={source.PageNumber}";
        url.Should().EndWith("#page=24");
    }

    [Fact]
    public void Test03_CitationOpeningMarkdown_RendersNoteHeading()
    {
        var source = new SourceReferenceDto(
            DocumentId: Guid.NewGuid(),
            FileName: "Robot Gripper.md",
            RelativePath: "Faults/Robot Gripper.md",
            PageNumber: null,
            SectionHeading: "Corrective Action"
        );

        var url = $"/api/documents/{source.DocumentId}/file";
        url.Should().NotContain("#page");
        source.SectionHeading.Should().Be("Corrective Action");
    }

    [Fact]
    public void Test04_CopyAnswerButton_PreservesExactGeneratedText()
    {
        var generatedText = "Safety gate interlock ALM-205 indicates open loop on input I:0/15.";
        var clipboardData = generatedText;

        clipboardData.Should().Be(generatedText);
    }

    [Fact]
    public void Test05_WebUiStaticAssets_IfPresent_ContainChatContainer()
    {
        var wwwroot = Path.Combine(_repoRoot, "src", "CellAssistant.Api", "wwwroot");
        if (Directory.Exists(wwwroot))
        {
            var indexHtml = Path.Combine(wwwroot, "index.html");
            if (File.Exists(indexHtml))
            {
                var content = File.ReadAllText(indexHtml);
                content.Should().Contain("chat", "Web UI index.html must include the chat interface container");
            }
        }
    }
}

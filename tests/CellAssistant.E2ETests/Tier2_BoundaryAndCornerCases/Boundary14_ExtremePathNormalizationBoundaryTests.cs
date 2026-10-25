using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier2_BoundaryAndCornerCases;

public class Boundary14_ExtremePathNormalizationBoundaryTests
{
    [Theory]
    [InlineData("Faults///Robot Gripper.md", "Faults///Robot Gripper.md")]
    [InlineData("Equipment\\Sub\\PLC.md", "Equipment/Sub/PLC.md")]
    [InlineData("Manuals/Robot.pdf/", "Manuals/Robot.pdf/")]
    [InlineData("/Alarms/ALM-205.md", "Alarms/ALM-205.md")]
    public void Test01_PathNormalization_ReplacesAllBackslashesAndTrimsLeadingSlash(string input, string expected)
    {
        var normalized = E2ETestHarness.NormalizeRelativePath(input);
        normalized.Should().Be(expected);
        normalized.Should().NotContain("\\");
        normalized.Should().NotStartWith("/");
    }

    [Fact]
    public void Test02_PathWithSpacesInDirectoryAndFilename_PreservesSpaceCharacters()
    {
        var input = "Lessons Learned\\Robot Cable Wear.md";
        var normalized = E2ETestHarness.NormalizeRelativePath(input);

        normalized.Should().Be("Lessons Learned/Robot Cable Wear.md");
    }

    [Fact]
    public void Test03_EmptyPath_ReturnsEmptyStringWithoutThrowing()
    {
        var normalized = E2ETestHarness.NormalizeRelativePath(string.Empty);
        normalized.Should().Be(string.Empty);
    }

    [Fact]
    public void Test04_PathWithUnicodeCharacters_PreservesCodepoints()
    {
        var input = "Procedures/Prüfung_Sicherheit.md";
        var normalized = E2ETestHarness.NormalizeRelativePath(input);

        normalized.Should().Be("Procedures/Prüfung_Sicherheit.md");
    }

    [Fact]
    public void Test05_WindowsDriveLetterInPath_IsTreatedAsRootOrDisallowed()
    {
        var input = "C:\\KnowledgeBase\\Faults\\Note.md";
        var normalized = E2ETestHarness.NormalizeRelativePath(input);

        normalized.Should().StartWith("C:/");
    }
}

using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier1_FeatureCoverage;

public class Feature14_ChunkHeaderNormalizationTests
{
    [Theory]
    [InlineData("Faults\\Robot Gripper.md", "Faults/Robot Gripper.md")]
    [InlineData("Equipment\\PLC\\Siemens.md", "Equipment/PLC/Siemens.md")]
    [InlineData("/Alarms/ALM-205.md", "Alarms/ALM-205.md")]
    [InlineData("\\Manuals\\Robot.pdf", "Manuals/Robot.pdf")]
    [InlineData("Procedures/Lockout.md", "Procedures/Lockout.md")]
    public void Test01_RelativePath_IsAlwaysNormalizedToPosixForwardSlashes(string input, string expected)
    {
        var normalized = E2ETestHarness.NormalizeRelativePath(input);
        normalized.Should().Be(expected);
        normalized.Should().NotContain("\\");
        normalized.Should().NotStartWith("/");
    }

    [Fact]
    public void Test02_ChunkHeader_FollowsExactBracketedSpecificationTemplate()
    {
        var header = E2ETestHarness.FormatChunkHeader(
            fileName: "VFD01 Inverter.md",
            folder: "Equipment",
            equipmentId: "VFD01",
            faultOrAlarmId: "FLT-004",
            pageOrHeading: "Overcurrent Trip",
            content: "Check motor winding resistance."
        );

        header.Should().StartWith("[Document: VFD01 Inverter.md | Folder: Equipment | Equipment: VFD01 | Fault/Alarm: FLT-004 | Page/Section: Overcurrent Trip]\nCheck motor winding resistance.");
    }

    [Fact]
    public void Test03_ChunkHeader_SubstitutesMissingFieldsWithFallback()
    {
        var header = E2ETestHarness.FormatChunkHeader(
            fileName: "GeneralNotes.txt",
            folder: "Maintenance",
            equipmentId: null,
            faultOrAlarmId: null,
            pageOrHeading: null,
            content: "Shift handover notes."
        );

        header.Should().Contain("Equipment: N/A");
        header.Should().Contain("Fault/Alarm: N/A");
        header.Should().Contain("Page/Section: N/A");
    }

    [Fact]
    public void Test04_ChunkHeader_PreservesOriginalContentIntegrity()
    {
        const string rawContent = "Special characters: & < > \" ' { } [ ] ~ ! @ # $ % ^ * ( ) _ + | \\";
        var header = E2ETestHarness.FormatChunkHeader("Test.md", "Procedures", "PLC01", null, "Special", rawContent);

        header.Should().EndWith(rawContent);
    }

    [Fact]
    public void Test05_NormalizedPaths_EnsureCrossPlatformHashConsistency()
    {
        var winPath = "Faults\\Robot Gripper.md";
        var linuxPath = "Faults/Robot Gripper.md";

        var normWin = E2ETestHarness.NormalizeRelativePath(winPath);
        var normLinux = E2ETestHarness.NormalizeRelativePath(linuxPath);

        normWin.Should().Be(normLinux);
    }
}

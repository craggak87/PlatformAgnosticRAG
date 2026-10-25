using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier2_BoundaryAndCornerCases;

public class Boundary04_IndustrialIdentifierBoundaryTests
{
    [Theory]
    [InlineData("alm-205", "ALM-205")]
    [InlineData("flt-001", "FLT-001")]
    [InlineData("vfd01", "VFD01")]
    [InlineData("robot01", "ROBOT01")]
    public void Test01_CaseInsensitiveExtraction_MatchesIdentifiers(string input, string expectedUppercase)
    {
        var ids = E2ETestHarness.ExtractIndustrialIdentifiers(input);
        ids.Should().Contain(expectedUppercase);
    }

    [Theory]
    [InlineData("Check ALM- with no digits")]
    [InlineData("FLT-XYZ invalid format")]
    [InlineData("ROBOT alone")]
    [InlineData("VFD without number")]
    [InlineData("PLC")]
    public void Test02_MalformedIncompleteIdentifiers_AreIgnored(string text)
    {
        var ids = E2ETestHarness.ExtractIndustrialIdentifiers(text);
        ids.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Fault occurred at ALM-205, please check.", "ALM-205")]
    [InlineData("Is I:0/15; energized?", "I:0/15")]
    [InlineData("Verify (ROBOT01) home.", "ROBOT01")]
    [InlineData("Inspect [FLT-001] immediately!", "FLT-001")]
    public void Test03_PunctuationAndBrackets_DoNotCorruptExtractedIdentifier(string text, string expectedId)
    {
        var ids = E2ETestHarness.ExtractIndustrialIdentifiers(text);
        ids.Should().Contain(expectedId);
    }

    [Theory]
    [InlineData("ROBOT9999")]
    [InlineData("VFD1234")]
    public void Test04_IdentifiersWithExcessiveDigits_AreIgnored(string text)
    {
        // Equipment IDs are strictly 2 digits per Doc 9 §10 (e.g. ROBOT01, VFD01)
        var ids = E2ETestHarness.ExtractIndustrialIdentifiers(text);
        ids.Should().NotContain(text);
    }

    [Fact]
    public void Test05_MultipleDifferentIdentifiersInSingleText_AllExtractedWithoutDuplicates()
    {
        var text = "ALM-205 on ROBOT01 due to I:0/15 sensor fault FLT-001 shown on drawing E102. Also ALM-205 again.";
        var ids = E2ETestHarness.ExtractIndustrialIdentifiers(text);

        ids.Should().HaveCount(5);
        ids.Should().Contain(new[] { "ALM-205", "ROBOT01", "I:0/15", "FLT-001", "E102" });
    }
}

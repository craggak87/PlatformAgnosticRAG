namespace CellAssistant.UnitTests.Domain;

using CellAssistant.Domain.Enums;
using CellAssistant.Domain.Services;
using CellAssistant.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

public class EmpiricalAdversarialStressTests
{
    private readonly IndustrialIdentifierExtractor _extractor = new();
    private readonly ConfidenceCalculator _calculator = new();

    private static RetrievedChunk CreateChunk(double score, bool exactMatch = false)
    {
        return new RetrievedChunk(
            Content: "Diagnostic content snippet for testing",
            SimilarityScore: score,
            ExactIdentifierMatch: exactMatch,
            Source: new SourceReference(Guid.NewGuid(), "Manual.md", "Manuals/Manual.md", "Manuals", 1, "Section 1")
        );
    }

    #region 1. IndustrialIdentifierExtractor Adversarial Verification

    [Theory]
    [InlineData("alm-205", "ALM-205")]
    [InlineData("aLm-205", "ALM-205")]
    [InlineData("ALm-205", "ALM-205")]
    [InlineData("flt-001", "FLT-001")]
    [InlineData("fLt-001", "FLT-001")]
    [InlineData("FLt-001", "FLT-001")]
    [InlineData("i:0/15", "I:0/15")]
    [InlineData("i1.15", "I1.15")]
    [InlineData("x12.05", "X12.05")]
    [InlineData("robot01", "ROBOT01")]
    [InlineData("rObOt01", "ROBOT01")]
    [InlineData("vfd01", "VFD01")]
    [InlineData("plc01", "PLC01")]
    [InlineData("conv01", "CONV01")]
    [InlineData("cam01", "CAM01")]
    [InlineData("hmi01", "HMI01")]
    [InlineData("e102", "E102")]
    [InlineData("p301", "P301")]
    public void Extractor_WeirdCasings_NormalizesToUppercase(string input, string expected)
    {
        var ids = _extractor.ExtractIdentifiers(input);
        ids.Should().Contain(expected);
    }

    [Theory]
    [InlineData(".ALM-205.", "ALM-205")]
    [InlineData(",,,FLT-001,,,", "FLT-001")]
    [InlineData("Target:ALM-205; Status:FLT-001;", "ALM-205", "FLT-001")]
    [InlineData("Is I:0/15 active?! Yes!", "I:0/15")]
    [InlineData("(ROBOT01) is connected to [VFD01]", "ROBOT01", "VFD01")]
    [InlineData("{\"alarm\": \"ALM-205\", \"fault\": \"FLT-001\"}", "ALM-205", "FLT-001")]
    [InlineData("<tag id=\"ALM-205\">I:0/15</tag>", "ALM-205", "I:0/15")]
    [InlineData("`I1.15` and `X12.05` and `E102`", "I1.15", "X12.05", "E102")]
    [InlineData("“ALM-205” ‘FLT-001’ ‘I:0/15’", "ALM-205", "FLT-001", "I:0/15")]
    [InlineData("**ALM-205** *ROBOT01* ***E102***", "ALM-205", "ROBOT01", "E102")]
    public void Extractor_PunctuationAndBrackets_ExtractsCorrectly(string input, params string[] expected)
    {
        var ids = _extractor.ExtractIdentifiers(input);
        foreach (var exp in expected)
        {
            ids.Should().Contain(exp);
        }
    }

    [Fact]
    public void Extractor_NestedWikiLinksAndTags_ExtractsCorrectly()
    {
        var text = "Refer to [[ALM-205]] and [[ROBOT01|Primary Robot]] with tag #FLT-001 and drawing [[E102]].";
        var ids = _extractor.ExtractIdentifiers(text);

        ids.Should().Contain(new[] { "ALM-205", "ROBOT01", "FLT-001", "E102" });
    }

    [Fact]
    public void Extractor_ComplexMultiLineText_ExtractsAllTargetTokens()
    {
        var multiLine = """
            ---
            equipment_id: ROBOT01
            fault_id: FLT-001
            alarm_id: ALM-205
            ---
            # Diagnostic Procedure
            
            Line 1: Verify power at terminal X12.05.
            Line 2: Check sensor I:0/15 connected to PLC module I1.15.
            Line 3: Cross-reference wiring on schematic E102.
            
            End of file.
            """;

        var ids = _extractor.ExtractIdentifiers(multiLine);

        ids.Should().Contain("ROBOT01");
        ids.Should().Contain("FLT-001");
        ids.Should().Contain("ALM-205");
        ids.Should().Contain("X12.05");
        ids.Should().Contain("I:0/15");
        ids.Should().Contain("I1.15");
        ids.Should().Contain("E102");
    }

    [Fact]
    public void Extractor_AllSevenTargetCategoriesFromDispatch_ExtractedProperly()
    {
        // Allen-Bradley I:0/15, Siemens I1.15, Mitsubishi/Beckhoff X12.05, Alarms ALM-205, Faults FLT-001, Equipment ROBOT01, Drawings E102
        var text = "Alarm ALM-205 and fault FLT-001 triggered on ROBOT01 due to I:0/15, I1.15, and X12.05 as per E102";
        var ids = _extractor.ExtractIdentifiers(text);

        ids.Should().HaveCount(7);
        ids.Should().BeEquivalentTo(new[]
        {
            "ALM-205",
            "FLT-001",
            "ROBOT01",
            "I:0/15",
            "I1.15",
            "X12.05",
            "E102"
        });
    }

    [Theory]
    [InlineData("ALM-")]
    [InlineData("FLT-")]
    [InlineData("ROBOT")]
    [InlineData("VFD")]
    [InlineData("PLC")]
    [InlineData("E1")]
    [InlineData("E12")]
    [InlineData("P9")]
    [InlineData("   ")]
    [InlineData("")]
    public void Extractor_MalformedOrIncomplete_ReturnsEmpty(string invalidText)
    {
        var ids = _extractor.ExtractIdentifiers(invalidText);
        ids.Should().BeEmpty();
    }

    [Fact]
    public void Extractor_MassiveInput_CompletesWithoutReDoSOrCorruption()
    {
        var repeatedNoise = string.Join(" ", Enumerable.Repeat("lorem ipsum dolor sit amet", 1000));
        var text = $"{repeatedNoise} ALM-205 {repeatedNoise} ROBOT01 {repeatedNoise} I:0/15 {repeatedNoise}";

        var ids = _extractor.ExtractIdentifiers(text);
        ids.Should().Contain(new[] { "ALM-205", "ROBOT01", "I:0/15" });
    }

    #endregion

    #region 2. ConfidenceCalculator Floating-Point Boundaries Verification

    [Fact]
    public void ConfidenceCalculator_Boundary08500_WithoutExactMatch_IsHigh()
    {
        var chunks = new[] { CreateChunk(0.8500, exactMatch: false) };
        var (score, level) = _calculator.Calculate(chunks, hasExactIdentifierMatch: false);

        score.Should().Be(0.8500);
        level.Should().Be(ConfidenceLevel.High);
    }

    [Fact]
    public void ConfidenceCalculator_Boundary08499_WithoutExactMatch_IsMedium()
    {
        var chunks = new[] { CreateChunk(0.8499, exactMatch: false) };
        var (score, level) = _calculator.Calculate(chunks, hasExactIdentifierMatch: false);

        score.Should().Be(0.8499);
        level.Should().Be(ConfidenceLevel.Medium);
    }

    [Fact]
    public void ConfidenceCalculator_Boundary07500_WithExactMatch_IsHigh()
    {
        var chunks = new[] { CreateChunk(0.7500, exactMatch: true) };
        var (score, level) = _calculator.Calculate(chunks, hasExactIdentifierMatch: true);

        score.Should().Be(0.7500);
        level.Should().Be(ConfidenceLevel.High);
    }

    [Fact]
    public void ConfidenceCalculator_Boundary07499_WithExactMatch_IsMedium()
    {
        var chunks = new[] { CreateChunk(0.7499, exactMatch: true) };
        var (score, level) = _calculator.Calculate(chunks, hasExactIdentifierMatch: true);

        score.Should().Be(0.7499);
        level.Should().Be(ConfidenceLevel.Medium);
    }

    [Fact]
    public void ConfidenceCalculator_Boundary07500_WithoutExactMatch_IsMedium()
    {
        var chunks = new[] { CreateChunk(0.7500, exactMatch: false) };
        var (score, level) = _calculator.Calculate(chunks, hasExactIdentifierMatch: false);

        score.Should().Be(0.7500);
        level.Should().Be(ConfidenceLevel.Medium);
    }

    [Fact]
    public void ConfidenceCalculator_Boundary07000_IsMedium()
    {
        var chunks = new[] { CreateChunk(0.7000, exactMatch: false) };
        var (score, level) = _calculator.Calculate(chunks, hasExactIdentifierMatch: false);

        score.Should().Be(0.7000);
        level.Should().Be(ConfidenceLevel.Medium);
    }

    [Fact]
    public void ConfidenceCalculator_Boundary06999_IsLow()
    {
        var chunks = new[] { CreateChunk(0.6999, exactMatch: false) };
        var (score, level) = _calculator.Calculate(chunks, hasExactIdentifierMatch: false);

        score.Should().Be(0.6999);
        level.Should().Be(ConfidenceLevel.Low);
    }

    [Fact]
    public void ConfidenceCalculator_ZeroScore_IsLow()
    {
        var chunks = new[] { CreateChunk(0.0000, exactMatch: false) };
        var (score, level) = _calculator.Calculate(chunks, hasExactIdentifierMatch: false);

        score.Should().Be(0.0000);
        level.Should().Be(ConfidenceLevel.Low);
    }

    [Fact]
    public void ConfidenceCalculator_EmptyChunks_ReturnsZeroScoreAndLow()
    {
        var (score1, level1) = _calculator.Calculate(Array.Empty<RetrievedChunk>(), hasExactIdentifierMatch: false);
        score1.Should().Be(0.0);
        level1.Should().Be(ConfidenceLevel.Low);

        var (score2, level2) = _calculator.Calculate(null, hasExactIdentifierMatch: false);
        score2.Should().Be(0.0);
        level2.Should().Be(ConfidenceLevel.Low);
    }

    [Fact]
    public void ConfidenceCalculator_NegativeScores_AlwaysEvaluateToLow()
    {
        var chunks = new[]
        {
            CreateChunk(-0.5, exactMatch: false),
            CreateChunk(-0.1, exactMatch: false)
        };

        var (score, level) = _calculator.Calculate(chunks, hasExactIdentifierMatch: false);
        level.Should().Be(ConfidenceLevel.Low);
    }

    [Fact]
    public void ConfidenceCalculator_NegativeScoreWithExactMatch_StillEvaluatesToLow()
    {
        var chunks = new[]
        {
            CreateChunk(-0.25, exactMatch: true)
        };

        var (score, level) = _calculator.Calculate(chunks, hasExactIdentifierMatch: true);
        level.Should().Be(ConfidenceLevel.Low);
    }

    #endregion
}

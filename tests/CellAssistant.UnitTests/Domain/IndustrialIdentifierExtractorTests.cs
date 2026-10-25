namespace CellAssistant.UnitTests.Domain;

using CellAssistant.Domain.Services;
using FluentAssertions;
using Xunit;

public class IndustrialIdentifierExtractorTests
{
    private readonly IndustrialIdentifierExtractor _extractor = new();

    [Theory]
    [InlineData("What causes alarm ALM-205 on the cell?", "ALM-205")]
    [InlineData("Investigate recurring fault ALM-102 on pneumatic station", "ALM-102")]
    [InlineData("Safety gate ALM-0001 triggered shutdown", "ALM-0001")]
    [InlineData("ALM-9999 warning active", "ALM-9999")]
    public void ExtractIdentifiers_AlarmCode_ReturnsNormalizedAlarm(string text, string expectedAlarm)
    {
        var ids = _extractor.ExtractIdentifiers(text);
        ids.Should().Contain(expectedAlarm);
    }

    [Theory]
    [InlineData("Check history for FLT-001 robot home failure", "FLT-001")]
    [InlineData("Resolve conveyor jam FLT-042", "FLT-042")]
    [InlineData("Encountered FLT-123 on cell entry", "FLT-123")]
    public void ExtractIdentifiers_FaultCode_ReturnsNormalizedFault(string text, string expectedFault)
    {
        var ids = _extractor.ExtractIdentifiers(text);
        ids.Should().Contain(expectedFault);
    }

    [Theory]
    [InlineData("Sensor at I:0/15 is off", "I:0/15")]
    [InlineData("Output relay O:1/2 energized", "O:1/2")]
    [InlineData("Solenoid mapped to O:0/2", "O:0/2")]
    [InlineData("Internal bit B3:0/5 cleared", "B3:0/5")]
    public void ExtractIdentifiers_AllenBradleyIo_ReturnsAddress(string text, string expectedAddress)
    {
        var ids = _extractor.ExtractIdentifiers(text);
        ids.Should().Contain(expectedAddress);
    }

    [Theory]
    [InlineData("Input I1.15 confirms gripper open", "I1.15")]
    [InlineData("Output Q0.2 mirrors state", "Q0.2")]
    [InlineData("Output Q0.4 enabled", "Q0.4")]
    [InlineData("Memory marker M10.1 set", "M10.1")]
    public void ExtractIdentifiers_SiemensIo_ReturnsAddress(string text, string expectedAddress)
    {
        var ids = _extractor.ExtractIdentifiers(text);
        ids.Should().Contain(expectedAddress);
    }

    [Theory]
    [InlineData("Check X12.05 signal", "X12.05")]
    [InlineData("Terminal output Y0.1 high", "Y0.1")]
    public void ExtractIdentifiers_BeckhoffIo_ReturnsAddress(string text, string expectedAddress)
    {
        var ids = _extractor.ExtractIdentifiers(text);
        ids.Should().Contain(expectedAddress);
    }

    [Fact]
    public void ExtractIdentifiers_EquipmentCodes_ReturnsAllStandardCodes()
    {
        var text = "ROBOT01 and VFD01 on CONV01 with PLC01, CAM01, HMI01";
        var ids = _extractor.ExtractIdentifiers(text);

        ids.Should().Contain(new[] { "ROBOT01", "VFD01", "CONV01", "PLC01", "CAM01", "HMI01" });
    }

    [Theory]
    [InlineData("Refer to drawing E102 and P301", "E102", "P301")]
    [InlineData("Schematics E205 and P4001", "E205", "P4001")]
    public void ExtractIdentifiers_DrawingNumbers_ReturnsDrawings(string text, string expected1, string expected2)
    {
        var ids = _extractor.ExtractIdentifiers(text);
        ids.Should().Contain(expected1);
        ids.Should().Contain(expected2);
    }

    [Fact]
    public void ExtractIdentifiers_MixedCaseAndDuplicates_DeDuplicatesAndNormalizes()
    {
        var text = "alm-205 ALM-205 robot01 ROBOT01 I:0/15 i:0/15";
        var ids = _extractor.ExtractIdentifiers(text);

        ids.Should().HaveCount(3);
        ids.Should().Contain(new[] { "ALM-205", "ROBOT01", "I:0/15" });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ExtractIdentifiers_NullOrWhitespace_ReturnsEmptyList(string? text)
    {
        var ids = _extractor.ExtractIdentifiers(text);
        ids.Should().BeEmpty();
    }

    [Fact]
    public void ExtractIdentifiers_NoMatches_ReturnsEmptyList()
    {
        var ids = _extractor.ExtractIdentifiers("How do I replace the lubrication oil on the main linear axis?");
        ids.Should().BeEmpty();
    }

    [Fact]
    public void ExtractIdentifiers_ComplexSentence_ExtractsAllExpectedTokens()
    {
        var text = "On ROBOT01, why did ALM-205 trigger input I:0/15 according to drawing E102?";
        var ids = _extractor.ExtractIdentifiers(text);

        ids.Should().Contain("ROBOT01");
        ids.Should().Contain("ALM-205");
        ids.Should().Contain("I:0/15");
        ids.Should().Contain("E102");
    }
}

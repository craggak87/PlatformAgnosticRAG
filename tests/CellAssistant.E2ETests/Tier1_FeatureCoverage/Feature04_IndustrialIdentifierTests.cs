using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier1_FeatureCoverage;

public class Feature04_IndustrialIdentifierTests
{
    [Theory]
    [InlineData("What causes alarm ALM-205 on the cell?", "ALM-205")]
    [InlineData("Investigate recurring fault ALM-102 on pneumatic station", "ALM-102")]
    [InlineData("Safety gate ALM-0001 triggered shutdown", "ALM-0001")]
    public void Test01_AlarmIdentifiers_AreExtractedAccurately(string text, string expectedAlarm)
    {
        var ids = E2ETestHarness.ExtractIndustrialIdentifiers(text);
        ids.Should().Contain(expectedAlarm);
    }

    [Theory]
    [InlineData("Check history for FLT-001 robot home failure", "FLT-001")]
    [InlineData("Resolve conveyor jam FLT-042", "FLT-042")]
    public void Test02_FaultIdentifiers_AreExtractedAccurately(string text, string expectedFault)
    {
        var ids = E2ETestHarness.ExtractIndustrialIdentifiers(text);
        ids.Should().Contain(expectedFault);
    }

    [Theory]
    [InlineData("Gripper cable sensor mapped to I:0/15", "I:0/15")]
    [InlineData("Siemens input I1.15 confirms clamp closed", "I1.15")]
    [InlineData("Beckhoff terminal input X12.05 status", "X12.05")]
    [InlineData("Output relay O:1/2 energized", "O:1/2")]
    [InlineData("Siemens output Q0.4 enabled", "Q0.4")]
    public void Test03_PlcInputOutputAddresses_AreExtractedAccurately(string text, string expectedAddress)
    {
        var ids = E2ETestHarness.ExtractIndustrialIdentifiers(text);
        ids.Should().Contain(expectedAddress);
    }

    [Theory]
    [InlineData("Where is VFD01 inverter located in cabinet?", "VFD01")]
    [InlineData("Calibrate ROBOT01 payload parameters", "ROBOT01")]
    [InlineData("Check CONV01 motor speed drift", "CONV01")]
    [InlineData("Inspect CAM01 vision lighting", "CAM01")]
    [InlineData("Check PLC01 CPU run mode", "PLC01")]
    public void Test04_EquipmentIdentifiers_AreExtractedAccurately(string text, string expectedEquipment)
    {
        var ids = E2ETestHarness.ExtractIndustrialIdentifiers(text);
        ids.Should().Contain(expectedEquipment);
    }

    [Theory]
    [InlineData("Refer to drawing E102 safety interlock circuit", "E102")]
    [InlineData("See electrical schematic E205 robot IO", "E205")]
    [InlineData("Pneumatic manifold drawing P301", "P301")]
    public void Test05_DrawingAndSchematicIdentifiers_AreExtractedAccurately(string text, string expectedDrawing)
    {
        var ids = E2ETestHarness.ExtractIndustrialIdentifiers(text);
        ids.Should().Contain(expectedDrawing);
    }
}

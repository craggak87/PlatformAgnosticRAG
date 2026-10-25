namespace CellAssistant.UnitTests.Parsing;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CellAssistant.Domain.Enums;
using CellAssistant.Infrastructure.Parsing;
using FluentAssertions;
using Xunit;

public class MarkdigYamlParserTests : IDisposable
{
    private readonly string _tempDir;
    private readonly MarkdigYamlParser _parser;

    public MarkdigYamlParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "markdig_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _parser = new MarkdigYamlParser();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Theory]
    [InlineData("note.md", true)]
    [InlineData("DOC.MD", true)]
    [InlineData("path/to/file.md", true)]
    [InlineData("file.pdf", false)]
    [InlineData("file.txt", false)]
    [InlineData("file.docx", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CanParse_ChecksExtensionCorrectly(string? path, bool expected)
    {
        _parser.CanParse(path!).Should().Be(expected);
    }

    [Fact]
    public async Task ParseAsync_StandardFrontmatter_ExtractsEquipmentAndManufacturer()
    {
        var content = @"---
equipment_id: ROBOT01
equipment_name: Robot Cell 1
manufacturer: ABB
model: IRB4600
location: Cell A
criticality: High
tags:
  - robot
  - safety
---
# Overview
This is an overview of the robot.";

        var filePath = Path.Combine(_tempDir, "robot.md");
        await File.WriteAllTextAsync(filePath, content);

        var (document, chunks) = await _parser.ParseAsync(filePath, "Equipment/robot.md");

        document.EquipmentId.Should().Be("ROBOT01");
        document.Manufacturer.Should().Be("ABB");
        document.Criticality.Should().Be("High");
        document.DocumentType.Should().Be(DocumentType.Markdown);
        document.Folder.Should().Be("Equipment");
        document.Tags.Should().Contain(new[] { "robot", "safety" });
        chunks.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ParseAsync_FaultAndAlarmFrontmatter_ExtractsFaultIdAndAlarmId()
    {
        var content = @"---
equipment_id: ROBOT01
fault_id: FLT-001
alarm_id: ALM-205
criticality: High
---
# Fault Details
Gripper timeout error on station 1.";

        var filePath = Path.Combine(_tempDir, "fault.md");
        await File.WriteAllTextAsync(filePath, content);

        var (document, chunks) = await _parser.ParseAsync(filePath, "Faults/fault.md");

        chunks.Should().NotBeEmpty();
        chunks[0].FaultId.Should().Be("FLT-001");
        chunks[0].AlarmId.Should().Be("ALM-205");
        chunks[0].Content.Should().Contain("Fault/Alarm: FLT-001");
    }

    [Fact]
    public async Task ParseAsync_InlineHashtags_ExtractedWithoutHash()
    {
        var content = @"---
equipment_id: CONV01
---
# Main Section
Motor overspeed detected. #conveyor #motor_fault #electrical";

        var filePath = Path.Combine(_tempDir, "tags.md");
        await File.WriteAllTextAsync(filePath, content);

        var (document, chunks) = await _parser.ParseAsync(filePath, "Equipment/tags.md");

        document.Tags.Should().Contain(new[] { "conveyor", "motor_fault", "electrical" });
        document.Tags.Should().NotContain("#conveyor");
    }

    [Fact]
    public async Task ParseAsync_Wikilinks_PreservedInChunkText()
    {
        var content = @"---
equipment_id: ROBOT01
---
# Details
See [[Pneumatic Gripper Manual]] and [[Safety Gate Procedures]].";

        var filePath = Path.Combine(_tempDir, "wikilinks.md");
        await File.WriteAllTextAsync(filePath, content);

        var (_, chunks) = await _parser.ParseAsync(filePath, "Equipment/wikilinks.md");

        chunks.Should().ContainSingle();
        chunks[0].Content.Should().Contain("[[Pneumatic Gripper Manual]]");
        chunks[0].Content.Should().Contain("[[Safety Gate Procedures]]");
    }

    [Fact]
    public async Task ParseAsync_HeadingAwareSplitting_CreatesSeparateChunksPerHeading()
    {
        var content = @"---
equipment_id: ROBOT01
---
# Robot Cell
General intro text.

## Symptoms
The robot stops suddenly at joint 3.

### Corrective Actions
Check encoder cabling and reset safety gate.";

        var filePath = Path.Combine(_tempDir, "headings.md");
        await File.WriteAllTextAsync(filePath, content);

        var (_, chunks) = await _parser.ParseAsync(filePath, "Faults/headings.md");

        chunks.Count.Should().Be(3);
        chunks[0].SectionHeading.Should().Be("Robot Cell");
        chunks[1].SectionHeading.Should().Be("Symptoms");
        chunks[2].SectionHeading.Should().Be("Corrective Actions");
    }

    [Fact]
    public async Task ParseAsync_FencedCodeBlockWithHash_DoesNotSplitSection()
    {
        var content = @"---
equipment_id: PLC01
---
# Logic Overview
Here is a Python diagnostic script:
```python
# comment line 1
# comment line 2
print('Testing PLC')
```
More text in this section.";

        var filePath = Path.Combine(_tempDir, "code.md");
        await File.WriteAllTextAsync(filePath, content);

        var (_, chunks) = await _parser.ParseAsync(filePath, "PLC/code.md");

        // Fenced code block comments should not create new heading chunks
        chunks.Should().ContainSingle();
        chunks[0].SectionHeading.Should().Be("Logic Overview");
    }

    [Fact]
    public async Task ParseAsync_OversizedSection_Applies500WordWindowWith100Overlap()
    {
        var words = string.Join(" ", Enumerable.Range(1, 800).Select(i => $"step{i}"));
        var content = $@"---
equipment_id: ROBOT01
---
# Long Procedure
{words}";

        var filePath = Path.Combine(_tempDir, "oversized.md");
        await File.WriteAllTextAsync(filePath, content);

        var (_, chunks) = await _parser.ParseAsync(filePath, "Procedures/oversized.md");

        // 800 words should produce 2 chunks (500 words, then 400 words step -> remaining 400 words)
        chunks.Count.Should().Be(2);
        chunks[0].SectionHeading.Should().Be("Long Procedure");
        chunks[1].SectionHeading.Should().Be("Long Procedure");
    }

    [Fact]
    public async Task ParseAsync_MalformedYaml_HandlesGracefullyWithoutCrash()
    {
        var content = @"---
equipment_id: ROBOT01
tags:
	- tab indented tag
description: Unquoted: Colon: Error
---
# Content
Document content despite malformed frontmatter.";

        var filePath = Path.Combine(_tempDir, "malformed.md");
        await File.WriteAllTextAsync(filePath, content);

        var (document, chunks) = await _parser.ParseAsync(filePath, "Notes/malformed.md");

        document.Should().NotBeNull();
        chunks.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ParseAsync_NoHeadings_TreatsEntireDocumentAsSingleSection()
    {
        var content = "This note has no headings at all, just plain paragraphs.";

        var filePath = Path.Combine(_tempDir, "no_headings.md");
        await File.WriteAllTextAsync(filePath, content);

        var (_, chunks) = await _parser.ParseAsync(filePath, "Notes/no_headings.md");

        chunks.Should().ContainSingle();
        chunks[0].SectionHeading.Should().BeNull();
    }

    [Fact]
    public async Task ParseAsync_ContextHeaders_PrependedToEveryChunk()
    {
        var content = @"---
equipment_id: ROBOT01
fault_id: FLT-001
---
# Symptoms
Low air pressure alarm triggered.";

        var filePath = Path.Combine(_tempDir, "header_test.md");
        await File.WriteAllTextAsync(filePath, content);

        var (_, chunks) = await _parser.ParseAsync(filePath, "Faults/header_test.md");

        chunks.Should().ContainSingle();
        chunks[0].Content.Should().StartWith("[Document: header_test.md | Folder: Faults | Equipment: ROBOT01 | Fault/Alarm: FLT-001 | Page/Section: Symptoms]\n");
    }

    [Fact]
    public async Task ParseAsync_FileNotFound_ThrowsFileNotFoundException()
    {
        var act = () => _parser.ParseAsync(Path.Combine(_tempDir, "non_existent.md"), "non_existent.md");
        await act.Should().ThrowAsync<FileNotFoundException>();
    }
}

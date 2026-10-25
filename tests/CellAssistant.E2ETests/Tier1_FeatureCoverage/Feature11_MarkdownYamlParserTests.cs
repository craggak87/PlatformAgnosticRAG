using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;
using YamlDotNet.Serialization;

namespace CellAssistant.E2ETests.Tier1_FeatureCoverage;

public class Feature11_MarkdownYamlParserTests
{
    private const string SampleMarkdownWithFrontmatter = @"---
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
# Robot Cell 1

## Purpose
Primary material handling robot for station 1. #material #handling

## Key Components
Includes [[Pneumatic Gripper]] and optical sensors.
";

    [Fact]
    public void Test01_YamlFrontmatter_ExtractsEquipmentAndManufacturer()
    {
        var yamlSegment = SampleMarkdownWithFrontmatter.Split("---")[1];
        var deserializer = new DeserializerBuilder().Build();
        var frontmatter = deserializer.Deserialize<Dictionary<string, object>>(yamlSegment);

        frontmatter["equipment_id"].ToString().Should().Be("ROBOT01");
        frontmatter["manufacturer"].ToString().Should().Be("ABB");
        frontmatter["criticality"].ToString().Should().Be("High");
    }

    [Fact]
    public void Test02_YamlTagsList_ExtractsCleanNormalizedTags()
    {
        var yamlSegment = SampleMarkdownWithFrontmatter.Split("---")[1];
        var deserializer = new DeserializerBuilder().Build();
        var frontmatter = deserializer.Deserialize<Dictionary<string, object>>(yamlSegment);

        var tags = (List<object>)frontmatter["tags"];
        tags.Select(t => t.ToString()).Should().Contain(new[] { "robot", "safety" });
    }

    [Fact]
    public void Test03_InlineHashtags_AreExtractedWithoutHashSymbol()
    {
        var text = "Primary material handling robot for station 1. #material #handling";
        var matches = System.Text.RegularExpressions.Regex.Matches(text, @"#([a-zA-Z0-9_\-]+)");
        var tags = matches.Select(m => m.Groups[1].Value).ToList();

        tags.Should().Contain(new[] { "material", "handling" });
        tags.Should().NotContain("#material");
    }

    [Fact]
    public void Test04_ObsidianWikiLinks_AreIdentifiedInContent()
    {
        var text = "Includes [[Pneumatic Gripper]] and optical sensors.";
        var matches = System.Text.RegularExpressions.Regex.Matches(text, @"\[\[(.*?)\]\]");
        var links = matches.Select(m => m.Groups[1].Value).ToList();

        links.Should().Contain("Pneumatic Gripper");
    }

    [Fact]
    public void Test05_Headings_SplitContentIntoSections()
    {
        var body = @"# Robot Cell 1
## Purpose
Content under purpose.
## Key Components
Content under components.";

        var sections = body.Split(new[] { "## " }, StringSplitOptions.RemoveEmptyEntries);
        sections.Length.Should().BeGreaterThan(1);
        sections[1].Should().StartWith("Purpose");
    }
}

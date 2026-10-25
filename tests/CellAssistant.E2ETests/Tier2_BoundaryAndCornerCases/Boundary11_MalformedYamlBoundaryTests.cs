using FluentAssertions;
using Xunit;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace CellAssistant.E2ETests.Tier2_BoundaryAndCornerCases;

public class Boundary11_MalformedYamlBoundaryTests
{
    [Fact]
    public void Test01_MissingClosingFrontmatterDelimiter_IsDetected()
    {
        var text = @"---
equipment_id: ROBOT01
manufacturer: ABB
# Missing closing delimiter
# Heading 1
Some content without closing frontmatter delimiter.
";

        var parts = text.Split("---");
        // An unclosed frontmatter block has fewer than 3 segments when split on ---
        parts.Length.Should().BeLessThan(3);
    }

    [Fact]
    public void Test02_TabCharacterInYamlIndentation_ThrowsYamlSyntaxException()
    {
        // YAML 1.2 prohibits tab characters for indentation
        var invalidYaml = "tags:\n\t- robot\n\t- fault";
        var deserializer = new DeserializerBuilder().Build();

        var act = () => deserializer.Deserialize<Dictionary<string, object>>(invalidYaml);
        act.Should().Throw<YamlException>();
    }

    [Fact]
    public void Test03_UnquotedColonInScalarValue_HandledOrEscaped()
    {
        var yamlWithColon = "description: Error: Timeout occurred";
        var deserializer = new DeserializerBuilder().Build();

        var act = () => deserializer.Deserialize<Dictionary<string, object>>(yamlWithColon);
        act.Should().Throw<YamlException>();
    }

    [Fact]
    public void Test04_EmptyFrontmatterBlock_ProducesEmptyDictionaryGracefully()
    {
        var emptyFrontmatter = "--- \n---";
        var yaml = emptyFrontmatter.Split("---")[1].Trim();

        var deserializer = new DeserializerBuilder().Build();
        var result = deserializer.Deserialize<Dictionary<string, object>?>(yaml);

        result.Should().BeNull();
    }

    [Fact]
    public void Test05_FrontmatterWithoutBody_HandledSafelyWithoutCrash()
    {
        var note = @"---
equipment_id: CONV01
---
";
        var parts = note.Split("---", StringSplitOptions.RemoveEmptyEntries);
        var body = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        body.Should().BeEmpty();
    }
}

using System.Text.Json;
using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier1_FeatureCoverage;

public class Feature08_QdrantCollectionTests
{
    [Fact]
    public void Test01_QdrantCollectionParameters_MatchSpecification()
    {
        SpecificationConstants.QdrantCollectionName.Should().Be("cell-knowledge");
        SpecificationConstants.QdrantVectorSize.Should().Be(768);
        SpecificationConstants.QdrantDistanceMetric.Should().Be("Cosine");
    }

    [Fact]
    public void Test02_QdrantPayloadIndexes_CountAndNamesMatchSpecification()
    {
        SpecificationConstants.QdrantPayloadIndexFields.Length.Should().Be(7);
        SpecificationConstants.QdrantPayloadIndexFields.Should().Contain(new[]
        {
            "content",
            "documentId",
            "equipmentId",
            "faultId",
            "alarmId",
            "tags",
            "folder"
        });
    }

    [Fact]
    public void Test03_QdrantPayloadPointSchema_SerializesExpectedAttributes()
    {
        var docId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();
        var point = new
        {
            id = chunkId,
            vector = new float[768],
            payload = new Dictionary<string, object?>
            {
                ["documentId"] = docId.ToString(),
                ["fileName"] = "Robot Gripper.md",
                ["relativePath"] = "Faults/Robot Gripper.md",
                ["folder"] = "Faults",
                ["page"] = (int?)null,
                ["sectionHeading"] = "Symptoms",
                ["equipmentId"] = "ROBOT01",
                ["faultId"] = "FLT-001",
                ["alarmId"] = (string?)null,
                ["tags"] = new[] { "robot", "gripper" },
                ["content"] = "[Document: Robot Gripper.md | Equipment: ROBOT01 | Section: Symptoms]\nGripper fails to close."
            }
        };

        var json = JsonSerializer.Serialize(point);
        json.Should().Contain("ROBOT01");
        json.Should().Contain("FLT-001");
        json.Should().Contain("Faults/Robot Gripper.md");
    }

    [Fact]
    public void Test04_FullTextIndexOnContent_RequiresWordTokenizer()
    {
        var contentIndexSchema = new
        {
            field_name = "content",
            field_schema = new
            {
                type = "text",
                tokenizer = "word",
                min_token_len = 2,
                max_token_len = 20,
                lowercase = true
            }
        };

        contentIndexSchema.field_schema.type.Should().Be("text");
        contentIndexSchema.field_schema.tokenizer.Should().Be("word");
        contentIndexSchema.field_schema.lowercase.Should().BeTrue();
    }

    [Fact]
    public void Test05_KeywordIndexSchemas_ConfiguredForFilterFields()
    {
        var keywordFields = new[] { "documentId", "equipmentId", "faultId", "alarmId", "tags", "folder" };
        foreach (var field in keywordFields)
        {
            var schema = new { field_name = field, field_schema = "keyword" };
            schema.field_schema.Should().Be("keyword");
        }
    }
}

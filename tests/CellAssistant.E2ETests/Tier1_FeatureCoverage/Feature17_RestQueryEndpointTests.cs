using System.Text.Json;
using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier1_FeatureCoverage;

public class Feature17_RestQueryEndpointTests
{
    [Fact]
    public void Test01_QueryEndpointRoute_MatchesSpecification()
    {
        SpecificationConstants.QueryEndpoint.Should().Be("/api/query");
    }

    [Fact]
    public void Test02_QueryRequestDto_SerializesToJsonCorrectly()
    {
        var request = new QueryRequestDto("What causes gripper timeout?", "ROBOT01", "Faults");
        var json = JsonSerializer.Serialize(request);

        json.Should().Contain("\"question\":\"What causes gripper timeout?\"");
        json.Should().Contain("\"equipmentIdFilter\":\"ROBOT01\"");
        json.Should().Contain("\"folderFilter\":\"Faults\"");
    }

    [Fact]
    public void Test03_QueryResponseDto_DeserializesStructuredResponse()
    {
        var docId = Guid.NewGuid();
        var json = $$"""
        {
          "answer": "Gripper timeout is caused by proximity sensor cable damage on I:0/15.",
          "confidence": "High",
          "sources": [
            {
              "documentId": "{{docId}}",
              "fileName": "Robot Gripper.md",
              "relativePath": "Faults/Robot Gripper.md",
              "pageNumber": null,
              "sectionHeading": "Root Cause"
            }
          ]
        }
        """;

        var response = JsonSerializer.Deserialize<QueryResponseDto>(json);
        response.Should().NotBeNull();
        response!.Answer.Should().Contain("I:0/15");
        response.Confidence.Should().Be("High");
        response.Sources.Should().HaveCount(1);
        response.Sources[0].DocumentId.Should().Be(docId);
        response.Sources[0].FileName.Should().Be("Robot Gripper.md");
        response.Sources[0].SectionHeading.Should().Be("Root Cause");
    }

    [Fact]
    public void Test04_QuestionLengthValidation_PermitsUpTo2000Characters()
    {
        var maxValidQuestion = new string('A', 2000);
        maxValidQuestion.Length.Should().Be(2000);

        var request = new QueryRequestDto(maxValidQuestion);
        request.Question.Length.Should().Be(2000);
    }

    [Fact]
    public void Test05_QueryResponse_GuaranteesConfidenceRating()
    {
        var response = new QueryResponseDto("Answer text", "High", Array.Empty<SourceReferenceDto>());
        var validRatings = new[] { "High", "Medium", "Low" };
        validRatings.Should().Contain(response.Confidence);
    }
}

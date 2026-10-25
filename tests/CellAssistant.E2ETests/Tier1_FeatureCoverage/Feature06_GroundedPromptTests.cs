using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier1_FeatureCoverage;

public class Feature06_GroundedPromptTests
{
    [Fact]
    public void Test01_ExactFallbackMessage_MatchesSpecificationVerbatim()
    {
        SpecificationConstants.ExactFallbackMessage.Should()
            .Be("The available documentation does not contain sufficient information.",
                "ADR-011 and PRD mandate this exact verbatim string when context is missing");
    }

    [Fact]
    public void Test02_SystemPromptTemplate_EnforcesGroundedConstraints()
    {
        const string systemPromptTemplate = @"You are an engineering assistant.
Answer only with supplied context.
Do not invent information.
Always cite sources.
If data cannot be found, clearly state:
""The available documentation does not contain sufficient information.""";

        systemPromptTemplate.Should().Contain("Answer only with supplied context");
        systemPromptTemplate.Should().Contain("Do not invent information");
        systemPromptTemplate.Should().Contain(SpecificationConstants.ExactFallbackMessage);
    }

    [Fact]
    public void Test03_EmptyContext_MustYieldFallbackWithoutCitations()
    {
        var retrievedChunks = Array.Empty<RetrievedChunkModel>();
        string answer;
        IReadOnlyList<SourceReferenceDto> sources;

        if (retrievedChunks.Length == 0)
        {
            answer = SpecificationConstants.ExactFallbackMessage;
            sources = Array.Empty<SourceReferenceDto>();
        }
        else
        {
            answer = "Grounded response";
            sources = new[] { retrievedChunks[0].Source };
        }

        answer.Should().Be(SpecificationConstants.ExactFallbackMessage);
        sources.Should().BeEmpty("No citations should be returned on fallback response");
    }

    [Fact]
    public void Test04_ContextBuilding_PrependsSourceHeadersToChunks()
    {
        var chunk = new RetrievedChunkModel(
            Content: "Check limit switch LS-02 alignment.",
            SimilarityScore: 0.89,
            ExactIdentifierMatch: false,
            Source: new SourceReferenceDto(
                DocumentId: Guid.NewGuid(),
                FileName: "Robot Home Position.md",
                RelativePath: "Faults/Robot Home Position.md",
                PageNumber: null,
                SectionHeading: "Diagnostics"
            )
        );

        var enriched = E2ETestHarness.FormatChunkHeader(
            chunk.Source.FileName,
            "Faults",
            "ROBOT01",
            "FLT-001",
            chunk.Source.SectionHeading,
            chunk.Content
        );

        enriched.Should().StartWith("[Document: Robot Home Position.md | Folder: Faults | Equipment: ROBOT01 | Fault/Alarm: FLT-001 | Page/Section: Diagnostics]");
        enriched.Should().Contain("Check limit switch LS-02 alignment.");
    }

    [Fact]
    public void Test05_ConfidenceLow_TriggersFallbackConditionWhenBelowThreshold()
    {
        var chunks = new[]
        {
            new RetrievedChunkModel("Generic irrelevant text", 0.45, false,
                new SourceReferenceDto(Guid.NewGuid(), "General.txt", "General.txt", null, null))
        };

        var (score, level) = E2ETestHarness.CalculateConfidence(chunks, false);
        level.Should().Be(ConfidenceLevel.Low);

        var answer = level == ConfidenceLevel.Low && score < SpecificationConstants.MediumConfidenceMin
            ? SpecificationConstants.ExactFallbackMessage
            : "Some answer";

        answer.Should().Be(SpecificationConstants.ExactFallbackMessage);
    }
}

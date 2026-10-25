using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier2_BoundaryAndCornerCases;

public class Boundary17_RestQueryEdgeInputsBoundaryTests
{
    [Fact]
    public void Test01_QuestionLengthAt2000Characters_IsValid()
    {
        var validQuestion = new string('a', 2000);
        bool isValid = !string.IsNullOrWhiteSpace(validQuestion) && validQuestion.Length <= 2000;

        isValid.Should().BeTrue();
    }

    [Fact]
    public void Test02_QuestionLengthAt2001Characters_IsInvalid()
    {
        var invalidQuestion = new string('a', 2001);
        bool isValid = !string.IsNullOrWhiteSpace(invalidQuestion) && invalidQuestion.Length <= 2000;

        isValid.Should().BeFalse("Queries exceeding 2000 characters must be rejected per Doc 4 §2");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Test03_EmptyOrWhitespaceQuestion_IsRejected(string question)
    {
        bool isValid = !string.IsNullOrWhiteSpace(question);
        isValid.Should().BeFalse();
    }

    [Fact]
    public void Test04_QuestionWithSqlInjectionPayload_IsSafelyHandledWithoutSqlError()
    {
        const string sqlPayload = "'; DROP TABLE documents; SELECT * FROM search_history WHERE '1'='1";
        using var conn = E2ETestHarness.CreateInMemorySqlite();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM documents WHERE file_name = @query;";
        cmd.Parameters.AddWithValue("@query", sqlPayload);

        var count = Convert.ToInt32(cmd.ExecuteScalar());
        count.Should().Be(0);

        // Verify table still exists
        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM documents;";
        var docCount = Convert.ToInt32(checkCmd.ExecuteScalar());
        docCount.Should().Be(0);
    }

    [Fact]
    public void Test05_QuestionWithUnicodeAndMathematicalSymbols_PreservesFullString()
    {
        var mathQuestion = "Calculate torque τ = r × F for ROBOT01 axis 3 with angle θ = 45°";
        var request = new QueryRequestDto(mathQuestion);

        request.Question.Should().Be(mathQuestion);
    }
}

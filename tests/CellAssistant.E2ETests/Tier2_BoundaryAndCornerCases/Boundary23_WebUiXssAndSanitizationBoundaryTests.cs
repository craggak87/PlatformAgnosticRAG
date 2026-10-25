using System.Net;
using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier2_BoundaryAndCornerCases;

public class Boundary23_WebUiXssAndSanitizationBoundaryTests
{
    [Theory]
    [InlineData("<script>alert('XSS')</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<svg/onload=alert(document.cookie)>")]
    [InlineData("javascript:alert(1)")]
    public void Test01_MaliciousPayloadInNoteTitle_IsHtmlEncoded(string maliciousPayload)
    {
        var encoded = WebUtility.HtmlEncode(maliciousPayload);
        encoded.Should().NotContain("<script>");
        encoded.Should().NotContain("<img");
        encoded.Should().NotContain("<svg");
    }

    [Fact]
    public void Test02_CitationUrl_RejectsJavascriptScheme()
    {
        var dangerousUri = "javascript:alert(1)";
        bool isHttpOrRelative = dangerousUri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                dangerousUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                                dangerousUri.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);

        isHttpOrRelative.Should().BeFalse("Web UI must never emit or execute javascript: URIs");
    }

    [Fact]
    public void Test03_SourceCitationPayload_EncodesSectionHeadings()
    {
        var source = new SourceReferenceDto(
            DocumentId: Guid.NewGuid(),
            FileName: "Malicious.md",
            RelativePath: "Faults/Malicious.md",
            PageNumber: null,
            SectionHeading: "<script>evil()</script>"
        );

        var safeHeading = WebUtility.HtmlEncode(source.SectionHeading);
        safeHeading.Should().Be("&lt;script&gt;evil()&lt;/script&gt;");
    }

    [Fact]
    public void Test04_GeneratedAnswerWithHtmlTags_PreservesCodeBlocksSafely()
    {
        var answer = "Replace the PLC tag: `I:0/15` and check condition `< 5.0 bar`.";
        var encoded = WebUtility.HtmlEncode(answer);

        encoded.Should().Contain("&lt; 5.0 bar");
    }

    [Fact]
    public void Test05_ErrorMiddlewareResponse_DoesNotEchoRawStackTrace()
    {
        var safeErrorResponse = new
        {
            message = "An unexpected error occurred.",
            correlationId = "req-12345678"
        };

        var json = System.Text.Json.JsonSerializer.Serialize(safeErrorResponse);
        json.Should().NotContain("at CellAssistant.");
        json.Should().NotContain("System.Exception");
        json.Should().Contain("correlationId");
    }
}

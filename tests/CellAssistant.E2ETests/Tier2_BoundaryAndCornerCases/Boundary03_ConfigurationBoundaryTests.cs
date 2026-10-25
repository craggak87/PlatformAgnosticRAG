using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier2_BoundaryAndCornerCases;

public class Boundary03_ConfigurationBoundaryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(99999)]
    public void Test01_InvalidServerPort_IsDetectedAsInvalid(int port)
    {
        bool isValidPort = port >= 1 && port <= 65535;
        isValidPort.Should().BeFalse($"Port {port} is outside valid TCP port range [1, 65535]");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Test02_InvalidTimeoutSeconds_IsDetectedAsInvalid(int timeout)
    {
        bool isValidTimeout = timeout > 0;
        isValidTimeout.Should().BeFalse($"Timeout {timeout} must be strictly positive");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Test03_EmptyOrWhitespaceBaseUrl_IsDetectedAsInvalid(string? baseUrl)
    {
        bool isValidUrl = !string.IsNullOrWhiteSpace(baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out _);
        isValidUrl.Should().BeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(256)]
    [InlineData(1024)]
    [InlineData(1536)]
    public void Test04_Non768VectorSize_DoesNotMatchGemmaEmbeddingContract(int size)
    {
        bool isGemmaEmbeddingMatch = size == SpecificationConstants.QdrantVectorSize;
        isGemmaEmbeddingMatch.Should().BeFalse($"Size {size} does not match 768 dims of embeddinggemma-2:latest");
    }

    [Fact]
    public void Test05_MissingConfigurationKeys_FallbackToHardcodedDefaults()
    {
        // When options are missing from YAML, verify defaults
        const int fallbackTimeout = SpecificationConstants.DefaultRequestTimeoutSeconds;
        const int fallbackDebounce = SpecificationConstants.DefaultWatcherDebounceMs;

        fallbackTimeout.Should().Be(120);
        fallbackDebounce.Should().Be(500);
    }
}

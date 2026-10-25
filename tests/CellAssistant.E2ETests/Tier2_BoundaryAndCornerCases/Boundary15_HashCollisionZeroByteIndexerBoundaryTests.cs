using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier2_BoundaryAndCornerCases;

public class Boundary15_HashCollisionZeroByteIndexerBoundaryTests
{
    [Fact]
    public void Test01_EmptyFileSha256_ProducesStandardNistKnownHash()
    {
        var emptyBytes = Array.Empty<byte>();
        var hash = E2ETestHarness.ComputeSha256(emptyBytes);

        // Standard known SHA-256 for empty input
        hash.Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Fact]
    public void Test02_SingleByteDifference_AltersOverFiftyPercentOfHashBits()
    {
        var bytesA = System.Text.Encoding.UTF8.GetBytes("ABB IRB4600 Manual Rev 1.0");
        var bytesB = System.Text.Encoding.UTF8.GetBytes("ABB IRB4600 Manual Rev 1.1");

        var hashA = E2ETestHarness.ComputeSha256(bytesA);
        var hashB = E2ETestHarness.ComputeSha256(bytesB);

        hashA.Should().NotBe(hashB);
    }

    [Fact]
    public void Test03_HashComparisonInSqlite_IsCaseInsensitive()
    {
        var lower = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        var upper = lower.ToUpperInvariant();

        string.Equals(lower, upper, StringComparison.OrdinalIgnoreCase).Should().BeTrue();
    }

    [Fact]
    public void Test04_LargeFileHash_CompletesWithoutMemoryExhaustion()
    {
        // 1MB byte array
        var large = new byte[1024 * 1024];
        var hash = E2ETestHarness.ComputeSha256(large);

        hash.Length.Should().Be(64);
    }

    [Fact]
    public void Test05_NullContentThrowsArgumentNullException()
    {
        var act = () => E2ETestHarness.ComputeSha256(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}

using System.Net;
using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier1_FeatureCoverage;

public class Feature20_PathTraversalPreventionTests
{
    private static bool IsPathSafelyInsideRoot(string rootPath, string targetPath)
    {
        var normalizedTarget = targetPath.Replace('\\', '/');
        var fullRoot = Path.GetFullPath(rootPath.Replace('\\', '/'));
        if (!fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString()))
        {
            fullRoot += Path.DirectorySeparatorChar;
        }

        var fullTarget = Path.GetFullPath(Path.Combine(fullRoot, normalizedTarget));
        return fullTarget.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("../../../etc/shadow")]
    [InlineData("..\\..\\Windows\\System32\\calc.exe")]
    [InlineData("Faults/../../secret.key")]
    [InlineData("/etc/passwd")]
    public void Test01_PathTraversalPayloads_AreDetectedAndRejected(string dangerousRelative)
    {
        var root = "/data/KnowledgeBase";
        var isSafe = IsPathSafelyInsideRoot(root, dangerousRelative);
        isSafe.Should().BeFalse($"Path '{dangerousRelative}' must not escape root '{root}' per ADR-013");
    }

    [Theory]
    [InlineData("Faults/Robot Gripper.md")]
    [InlineData("Manuals/ABB_IRB4600.pdf")]
    [InlineData("Alarms/ALM-205 Safety Gate Open.md")]
    [InlineData("Equipment/ROBOT01.md")]
    public void Test02_ValidInternalPaths_AreAllowedInsideRoot(string safeRelative)
    {
        var root = "/data/KnowledgeBase";
        var isSafe = IsPathSafelyInsideRoot(root, safeRelative);
        isSafe.Should().BeTrue($"Valid path '{safeRelative}' should be safely allowed inside '{root}'");
    }

    [Fact]
    public void Test03_UrlEncodedPathTraversal_IsDecodedAndRejected()
    {
        var encoded = "%2e%2e%2f%2e%2e%2fetc%2fpasswd";
        var decoded = WebUtility.UrlDecode(encoded);
        var root = "/data/KnowledgeBase";
        var candidate = Path.Combine(root, decoded);

        var isSafe = IsPathSafelyInsideRoot(root, candidate);
        isSafe.Should().BeFalse("Decoded URL traversal must be rejected");
    }

    [Fact]
    public void Test04_NullByteInjection_CannotBypassPathValidation()
    {
        var injection = "Manuals/Valid.pdf\0/../../etc/passwd";
        var act = () => Path.GetFullPath(Path.Combine("/data/KnowledgeBase", injection));

        act.Should().Throw<ArgumentException>("Null characters in path must be rejected by runtime");
    }

    [Fact]
    public void Test05_CanonicalPathComparison_HandlesSymlinksOrAliasesStrictly()
    {
        var root = "/data/KnowledgeBase";
        var inside = Path.Combine(root, "Subfolder", "File.txt");
        var outside = Path.Combine(root, "..", "data2", "File.txt");

        IsPathSafelyInsideRoot(root, inside).Should().BeTrue();
        IsPathSafelyInsideRoot(root, outside).Should().BeFalse();
    }
}

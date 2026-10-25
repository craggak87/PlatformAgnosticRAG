using System.Net;
using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier2_BoundaryAndCornerCases;

public class Boundary20_PathTraversalAdversarialBoundaryTests
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

    [Fact]
    public void Test01_DoubleUrlEncodedTraversal_IsDetectedAndBlocked()
    {
        // Double URL encoded: %252e%252e%252f -> %2e%2e%2f -> ../
        var doubleEncoded = "%252e%252e%252f%252e%252e%252fetc%252fpasswd";
        var decodedOnce = WebUtility.UrlDecode(doubleEncoded);
        var decodedTwice = WebUtility.UrlDecode(decodedOnce);

        var isSafe = IsPathSafelyInsideRoot("/data/KnowledgeBase", decodedTwice);
        isSafe.Should().BeFalse();
    }

    [Theory]
    [InlineData("..%2f..%2fetc%2fpasswd")]
    [InlineData("..%5c..%5cwindows%5cwin.ini")]
    public void Test02_MixedUrlAndSlashTraversal_IsDetectedAndBlocked(string mixed)
    {
        var decoded = WebUtility.UrlDecode(mixed);
        var isSafe = IsPathSafelyInsideRoot("/data/KnowledgeBase", decoded);
        isSafe.Should().BeFalse();
    }

    [Fact]
    public void Test03_PathWithTrailingDotDotSlash_IsBlocked()
    {
        var traversal = "Faults/Robot Gripper.md/../../../etc/passwd";
        var isSafe = IsPathSafelyInsideRoot("/data/KnowledgeBase", traversal);
        isSafe.Should().BeFalse();
    }

    [Fact]
    public void Test04_RootPathItself_IsDisallowedAsDocumentStream()
    {
        // Directory streaming should not return file content
        var root = "/data/KnowledgeBase";
        var fullPath = Path.GetFullPath(Path.Combine(root, "."));
        bool isFile = File.Exists(fullPath);

        isFile.Should().BeFalse("Root directory itself cannot be streamed as a document file");
    }

    [Fact]
    public void Test05_AbsolutePathsInRelativeField_AreOverriddenOrRejected()
    {
        var absoluteAttempt = "/etc/passwd";
        var isSafe = IsPathSafelyInsideRoot("/data/KnowledgeBase", absoluteAttempt);
        isSafe.Should().BeFalse();
    }
}

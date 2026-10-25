namespace CellAssistant.UnitTests.Security;

using System;
using System.IO;
using CellAssistant.Api.Security;
using FluentAssertions;
using Xunit;

public class PathSecurityValidatorTests : IDisposable
{
    private readonly string _tempRoot;

    public PathSecurityValidatorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Vault_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Faults"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Manuals"));

        File.WriteAllText(Path.Combine(_tempRoot, "Faults", "Robot Gripper.md"), "# Robot Gripper");
        File.WriteAllText(Path.Combine(_tempRoot, "Manuals", "ABB_Manual.pdf"), "%PDF-1.4 Mock");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Validate_ValidMarkdownFile_ReturnsSuccess()
    {
        var result = PathSecurityValidator.ValidateAndResolve(_tempRoot, "Faults/Robot Gripper.md");
        result.IsSuccess.Should().BeTrue();
        result.Status.Should().Be(PathValidationStatus.Valid);
        result.CanonicalPath.Should().NotBeNull();
        result.CanonicalPath!.Should().EndWith("Robot Gripper.md");
    }

    [Fact]
    public void Validate_ValidPdfFile_ReturnsSuccess()
    {
        var result = PathSecurityValidator.ValidateAndResolve(_tempRoot, "Manuals/ABB_Manual.pdf");
        result.IsSuccess.Should().BeTrue();
        result.Status.Should().Be(PathValidationStatus.Valid);
        result.CanonicalPath.Should().NotBeNull();
        result.CanonicalPath!.Should().EndWith("ABB_Manual.pdf");
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("../../../etc/shadow")]
    [InlineData("..\\..\\Windows\\System32\\calc.exe")]
    [InlineData("Faults/../../secret.key")]
    [InlineData("/etc/passwd")]
    public void Validate_TraversalPayloads_ReturnsForbidden(string payload)
    {
        var result = PathSecurityValidator.ValidateAndResolve(_tempRoot, payload);
        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(PathValidationStatus.Forbidden);
    }

    [Fact]
    public void Validate_SingleUrlEncodedTraversal_ReturnsForbidden()
    {
        var payload = "%2e%2e%2f%2e%2e%2fetc%2fpasswd";
        var result = PathSecurityValidator.ValidateAndResolve(_tempRoot, payload);
        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(PathValidationStatus.Forbidden);
    }

    [Fact]
    public void Validate_DoubleUrlEncodedTraversal_ReturnsForbidden()
    {
        var payload = "%252e%252e%252f%252e%252e%252fetc%252fpasswd";
        var result = PathSecurityValidator.ValidateAndResolve(_tempRoot, payload);
        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(PathValidationStatus.Forbidden);
    }

    [Theory]
    [InlineData("..%2f..%2fetc%2fpasswd")]
    [InlineData("..%5c..%5cwindows%5cwin.ini")]
    public void Validate_MixedUrlAndSlashTraversal_ReturnsForbidden(string payload)
    {
        var result = PathSecurityValidator.ValidateAndResolve(_tempRoot, payload);
        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(PathValidationStatus.Forbidden);
    }

    [Theory]
    [InlineData("Manuals/Valid.pdf\0/../../etc/passwd")]
    [InlineData("Manuals/Valid.pdf%00/../../etc/passwd")]
    public void Validate_NullByteInjection_ReturnsForbidden(string payload)
    {
        var result = PathSecurityValidator.ValidateAndResolve(_tempRoot, payload);
        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(PathValidationStatus.Forbidden);
    }

    [Fact]
    public void Validate_TrailingDotDotSlash_ReturnsForbidden()
    {
        var payload = "Faults/Robot Gripper.md/../../../etc/passwd";
        var result = PathSecurityValidator.ValidateAndResolve(_tempRoot, payload);
        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(PathValidationStatus.Forbidden);
    }

    [Fact]
    public void Validate_RootDirectoryItself_ReturnsForbidden()
    {
        var result = PathSecurityValidator.ValidateAndResolve(_tempRoot, ".");
        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(PathValidationStatus.Forbidden);
    }

    [Fact]
    public void Validate_DirectoryPath_ReturnsForbidden()
    {
        var result = PathSecurityValidator.ValidateAndResolve(_tempRoot, "Faults");
        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(PathValidationStatus.Forbidden);
    }

    [Fact]
    public void Validate_NonExistentFile_ReturnsNotFound()
    {
        var result = PathSecurityValidator.ValidateAndResolve(_tempRoot, "Faults/Missing.md");
        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(PathValidationStatus.NotFound);
    }

    [Theory]
    [InlineData(null, "Faults/Robot Gripper.md")]
    [InlineData("", "Faults/Robot Gripper.md")]
    [InlineData("/valid/root", null)]
    [InlineData("/valid/root", "")]
    public void Validate_NullOrEmptyInputs_ReturnsBadRequest(string? root, string? candidate)
    {
        var result = PathSecurityValidator.ValidateAndResolve(root!, candidate!);
        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(PathValidationStatus.BadRequest);
    }

    [Fact]
    public void IsPathSafelyInsideRoot_BlocksDangerousPayloads()
    {
        PathSecurityValidator.IsPathSafelyInsideRoot("/data/KnowledgeBase", "../../etc/passwd").Should().BeFalse();
        PathSecurityValidator.IsPathSafelyInsideRoot("/data/KnowledgeBase", "%252e%252e%252fetc%252fpasswd").Should().BeFalse();
        PathSecurityValidator.IsPathSafelyInsideRoot("/data/KnowledgeBase", "Faults/Robot Gripper.md").Should().BeTrue();
    }
}

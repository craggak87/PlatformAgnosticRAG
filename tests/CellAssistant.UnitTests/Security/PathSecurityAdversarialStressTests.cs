namespace CellAssistant.UnitTests.Security;

using System;
using System.IO;
using CellAssistant.Api.Security;
using FluentAssertions;
using Xunit;

public class PathSecurityAdversarialStressTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _siblingRoot;

    public PathSecurityAdversarialStressTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "VaultSec_" + Guid.NewGuid().ToString("N"));
        _siblingRoot = _tempRoot + "_sibling";

        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Equipment"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Faults"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Manuals"));

        Directory.CreateDirectory(_siblingRoot);
        File.WriteAllText(Path.Combine(_siblingRoot, "secret.key"), "SUPER_SECRET_TOKEN");

        File.WriteAllText(Path.Combine(_tempRoot, "Faults", "Robot Gripper.md"), "# Robot Gripper");
        File.WriteAllText(Path.Combine(_tempRoot, "Equipment", "ROBOT01.md"), "# ROBOT01 Specs");
        File.WriteAllText(Path.Combine(_tempRoot, "Manuals", "ABB_IRB4600.pdf"), "%PDF-1.4 Mock ABB");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        if (Directory.Exists(_siblingRoot))
        {
            Directory.Delete(_siblingRoot, recursive: true);
        }
    }

    #region 1. Deep Nested Traversal Payloads

    [Theory]
    [InlineData("../../../../../../../../../../../../etc/passwd")]
    [InlineData("../../../../../../../../../../../../etc/shadow")]
    [InlineData("../../../../../../../../../../../../etc/hosts")]
    [InlineData("..\\..\\..\\..\\..\\..\\..\\..\\..\\..\\..\\..\\etc\\passwd")]
    [InlineData("Equipment/../../../../../../../../../../../../etc/passwd")]
    [InlineData("Equipment/ROBOT01.md/../../../../../../../../../../../../etc/passwd")]
    [InlineData("Faults/Robot Gripper.md/../../../../../../../../../../../../etc/shadow")]
    [InlineData("Manuals/ABB_IRB4600.pdf/../../../../../../../../../../../../etc/hosts")]
    [InlineData("a/b/c/d/e/f/g/h/i/j/../../../../../../../../../../../../etc/passwd")]
    [InlineData("..///////..///////..///////..///////..///////etc/passwd")]
    [InlineData("..\\\\\\\\..\\\\\\\\..\\\\\\\\etc\\\\passwd")]
    public void Validate_DeepNestedTraversals_AreStrictlyForbidden(string payload)
    {
        var result = PathSecurityValidator.ValidateAndResolve(_tempRoot, payload);

        result.IsSuccess.Should().BeFalse($"Payload '{payload}' must be blocked.");
        result.Status.Should().Be(PathValidationStatus.Forbidden);
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region 2. Mixed Slashes and URL-Encoding Traversal Payloads

    [Theory]
    [InlineData("..%2f..%5c..%2fetc/passwd")]
    [InlineData("..%5c..%2f..%5cetc%2fpasswd")]
    [InlineData("%252e%252e%252f%252e%252e%252fetc%252fpasswd")]
    [InlineData("%252e%252e%255c%252e%252e%255cetc%255cpasswd")]
    [InlineData("%25252e%25252e%25252f%25252e%25252e%25252fetc%25252fpasswd")] // Triple URL-encoded
    [InlineData("..%252f..%252fetc/passwd")]
    [InlineData("..%255c..%255cetc/passwd")]
    [InlineData("%2e%2e/etc/passwd")]
    [InlineData("..%2fetc/passwd")]
    [InlineData("..%5cetc/passwd")]
    [InlineData("Equipment/%2e%2e/%2e%2e/etc/passwd")]
    [InlineData("Equipment/%252e%252e/%252e%252e/etc/passwd")]
    [InlineData("Faults/..%2f..%2fetc/shadow")]
    [InlineData("Faults/..%5c..%5cetc/shadow")]
    [InlineData("..%2f..%2f..%2f..%2f..%2f..%2f..%2f..%2fetc%2fpasswd")]
    public void Validate_MixedSlashesAndEncoding_AreStrictlyForbidden(string payload)
    {
        var result = PathSecurityValidator.ValidateAndResolve(_tempRoot, payload);

        result.IsSuccess.Should().BeFalse($"Payload '{payload}' must be blocked.");
        result.Status.Should().Be(PathValidationStatus.Forbidden);
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region 3. Null-Byte Variations and Injections

    [Theory]
    [InlineData("\0")]
    [InlineData("%00")]
    [InlineData("\0../../../../etc/passwd")]
    [InlineData("%00/../../etc/passwd")]
    [InlineData("Faults/Robot Gripper.md\0")]
    [InlineData("Faults/Robot Gripper.md\0.txt")]
    [InlineData("Faults/Robot Gripper.md\0.exe")]
    [InlineData("Faults/Robot Gripper.md%00")]
    [InlineData("Faults/Robot Gripper.md%00.pdf")]
    [InlineData("Faults\0/Robot Gripper.md")]
    [InlineData("Faults%00/Robot Gripper.md")]
    [InlineData("Faults/Robot Gripper.md%2500")] // Double-encoded null byte
    [InlineData("Faults/Robot Gripper.md%252500")] // Triple-encoded null byte
    [InlineData("\0Equipment/ROBOT01.md")]
    [InlineData("%00Equipment/ROBOT01.md")]
    [InlineData("Equipment/ROBOT01.md\0\0\0")]
    public void Validate_NullByteVariations_AreStrictlyForbidden(string payload)
    {
        var result = PathSecurityValidator.ValidateAndResolve(_tempRoot, payload);

        result.IsSuccess.Should().BeFalse($"Null-byte payload '{payload}' must be blocked.");
        result.Status.Should().Be(PathValidationStatus.Forbidden);
        result.ErrorMessage.Should().Contain("null byte");
    }

    #endregion

    #region 4. Self-Referencing Paths

    [Theory]
    [InlineData("././././../secret")]
    [InlineData("././././././././././../secret")]
    [InlineData("Faults/../../Faults/Robot Gripper.md")]
    [InlineData("Equipment/../../secret")]
    [InlineData("././././../etc/passwd")]
    [InlineData("Equipment/../Equipment/../Equipment/../../etc/passwd")]
    public void Validate_SelfReferencingEscapingPaths_AreStrictlyForbidden(string payload)
    {
        var result = PathSecurityValidator.ValidateAndResolve(_tempRoot, payload);

        result.IsSuccess.Should().BeFalse($"Self-referencing escaping payload '{payload}' must be blocked.");
        result.Status.Should().Be(PathValidationStatus.Forbidden);
    }

    [Theory]
    [InlineData("./././Faults/Robot Gripper.md")]
    [InlineData("./Faults/Robot Gripper.md")]
    [InlineData("Faults/./Robot Gripper.md")]
    [InlineData("Faults/../Faults/Robot Gripper.md")]
    [InlineData("Equipment/../Faults/Robot Gripper.md")]
    public void Validate_SafeSelfReferencingPathsInsideRoot_ResolveSuccessfully(string payload)
    {
        var result = PathSecurityValidator.ValidateAndResolve(_tempRoot, payload);

        result.IsSuccess.Should().BeTrue($"Safe self-referencing path '{payload}' should resolve successfully.");
        result.Status.Should().Be(PathValidationStatus.Valid);
        result.CanonicalPath.Should().NotBeNull();
        File.Exists(result.CanonicalPath).Should().BeTrue();
    }

    #endregion

    #region 5. Direct Directory Access Attempts

    [Theory]
    [InlineData("/")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("./")]
    [InlineData("../")]
    [InlineData("Faults")]
    [InlineData("Faults/")]
    [InlineData("Equipment")]
    [InlineData("Equipment/")]
    [InlineData("Manuals")]
    [InlineData("Manuals/")]
    [InlineData("Faults/.")]
    [InlineData("Faults/..")]
    [InlineData("././././.")]
    public void Validate_DirectDirectoryAccess_AreStrictlyForbidden(string directoryCandidate)
    {
        var result = PathSecurityValidator.ValidateAndResolve(_tempRoot, directoryCandidate);

        result.IsSuccess.Should().BeFalse($"Directory access '{directoryCandidate}' must not be served as a file.");
        result.Status.Should().Be(PathValidationStatus.Forbidden);
    }

    #endregion

    #region 6. Vault Sibling Prefix Attack (Prefix Matching Defense)

    [Fact]
    public void Validate_SiblingDirectoryWithCommonPrefix_IsStrictlyForbidden()
    {
        // Attack scenario: If root is /tmp/VaultSec_xxx, attacker attempts /tmp/VaultSec_xxx_sibling/secret.key
        var siblingSecret = Path.Combine(_siblingRoot, "secret.key");

        var result = PathSecurityValidator.ValidateAndResolve(_tempRoot, siblingSecret);

        result.IsSuccess.Should().BeFalse("Accessing sibling directory with same prefix must be blocked.");
        result.Status.Should().Be(PathValidationStatus.Forbidden);
    }

    [Fact]
    public void Validate_RelativeTraversalToSiblingDirectory_IsStrictlyForbidden()
    {
        string siblingName = Path.GetFileName(_siblingRoot);
        var payload = $"../{siblingName}/secret.key";

        var result = PathSecurityValidator.ValidateAndResolve(_tempRoot, payload);

        result.IsSuccess.Should().BeFalse("Relative traversal to sibling directory must be blocked.");
        result.Status.Should().Be(PathValidationStatus.Forbidden);
    }

    #endregion

    #region 7. Symlink Escape Security

    [Fact]
    public void Validate_SymlinkPointingOutsideRoot_IsStrictlyForbidden()
    {
        string symlinkPath = Path.Combine(_tempRoot, "Faults", "OutsideSymlink.md");
        try
        {
            File.CreateSymbolicLink(symlinkPath, Path.Combine(_siblingRoot, "secret.key"));
        }
        catch
        {
            // If OS or permissions do not permit symlink creation in test environment, skip assertion
            return;
        }

        var result = PathSecurityValidator.ValidateAndResolve(_tempRoot, "Faults/OutsideSymlink.md");

        result.IsSuccess.Should().BeFalse("Symlink pointing outside vault root must be rejected.");
        result.Status.Should().Be(PathValidationStatus.Forbidden);
        result.ErrorMessage.Should().Contain("unauthorized path outside the vault");
    }

    [Fact]
    public void Validate_SymlinkPointingInsideRoot_IsAllowed()
    {
        string symlinkPath = Path.Combine(_tempRoot, "Manuals", "GripperLink.md");
        try
        {
            File.CreateSymbolicLink(symlinkPath, Path.Combine(_tempRoot, "Faults", "Robot Gripper.md"));
        }
        catch
        {
            return;
        }

        var result = PathSecurityValidator.ValidateAndResolve(_tempRoot, "Manuals/GripperLink.md");

        result.IsSuccess.Should().BeTrue("Symlink pointing inside vault root should be allowed.");
        result.Status.Should().Be(PathValidationStatus.Valid);
    }

    #endregion

    #region 8. Non-Existent Files and Edge Cases

    [Theory]
    [InlineData("Faults/DoesNotExist.md")]
    [InlineData("Equipment/DoesNotExist.pdf")]
    [InlineData("NonExistentFolder/DoesNotExist.md")]
    public void Validate_NonExistentFilesInsideRoot_ReturnsNotFound(string nonExistentPath)
    {
        var result = PathSecurityValidator.ValidateAndResolve(_tempRoot, nonExistentPath);

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(PathValidationStatus.NotFound);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Validate_WhitespaceOnlyPaths_ReturnsBadRequest(string whitespace)
    {
        var result = PathSecurityValidator.ValidateAndResolve(_tempRoot, whitespace);

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(PathValidationStatus.BadRequest);
    }

    #endregion
}

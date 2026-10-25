namespace CellAssistant.Api.Security;

using System;
using System.IO;
using System.Net;

public enum PathValidationStatus
{
    Valid,
    Forbidden,
    NotFound,
    BadRequest
}

public sealed record PathValidationResult(
    PathValidationStatus Status,
    string? CanonicalPath,
    string? ErrorMessage
)
{
    public bool IsSuccess => Status == PathValidationStatus.Valid;

    public static PathValidationResult Success(string canonicalPath) =>
        new(PathValidationStatus.Valid, canonicalPath, null);

    public static PathValidationResult Forbidden(string message) =>
        new(PathValidationStatus.Forbidden, null, message);

    public static PathValidationResult NotFound(string message) =>
        new(PathValidationStatus.NotFound, null, message);

    public static PathValidationResult BadRequest(string message) =>
        new(PathValidationStatus.BadRequest, null, message);
}

public static class PathSecurityValidator
{
    private const int MaxDecodingPasses = 3;

    public static PathValidationResult ValidateAndResolve(string rootDirectory, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            return PathValidationResult.BadRequest("Vault root directory is not configured.");
        }

        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return PathValidationResult.BadRequest("Requested file path is missing.");
        }

        // 1. Immediate rejection of null byte characters
        if (candidatePath.IndexOf('\0') >= 0 || candidatePath.Contains("%00", StringComparison.OrdinalIgnoreCase))
        {
            return PathValidationResult.Forbidden("Path contains prohibited null byte characters.");
        }

        // 2. Iterative URL-decoding to eliminate double/multiple URL-encoding (%252e%252e%252f)
        string decoded = candidatePath;
        try
        {
            for (int i = 0; i < MaxDecodingPasses; i++)
            {
                string next = WebUtility.UrlDecode(decoded);
                if (next == decoded) break;
                decoded = next;
            }
        }
        catch (Exception ex)
        {
            return PathValidationResult.Forbidden($"Invalid URL encoding in path: {ex.Message}");
        }

        // Check again for null bytes post-decoding
        if (decoded.IndexOf('\0') >= 0)
        {
            return PathValidationResult.Forbidden("Path contains prohibited null byte characters after decoding.");
        }

        // 3. Resolve canonical root directory with trailing separator
        string fullRoot;
        try
        {
            fullRoot = Path.GetFullPath(rootDirectory.Replace('\\', '/'));
        }
        catch (Exception ex)
        {
            return PathValidationResult.BadRequest($"Invalid root directory path: {ex.Message}");
        }

        if (!fullRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            fullRoot += Path.DirectorySeparatorChar;
        }

        // 4. Resolve candidate full path
        string fullTarget;
        try
        {
            // Normalize slashes
            string normalized = decoded.Replace('\\', '/');

            if (Path.IsPathRooted(normalized))
            {
                fullTarget = Path.GetFullPath(normalized);
            }
            else
            {
                fullTarget = Path.GetFullPath(Path.Combine(fullRoot, normalized));
            }
        }
        catch (ArgumentException)
        {
            return PathValidationResult.Forbidden("Path contains invalid characters.");
        }
        catch (Exception ex)
        {
            return PathValidationResult.Forbidden($"Failed to resolve canonical path: {ex.Message}");
        }

        // 5. Strict canonical containment check
        if (!fullTarget.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            return PathValidationResult.Forbidden($"Access denied: Path '{candidatePath}' escapes the vault boundary.");
        }

        // 6. Prohibit streaming the root directory itself
        if (fullTarget.TrimEnd(Path.DirectorySeparatorChar).Equals(
            fullRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return PathValidationResult.Forbidden("Root directory itself cannot be accessed as a document.");
        }

        // 7. Verify file exists on disk
        if (!File.Exists(fullTarget))
        {
            if (Directory.Exists(fullTarget))
            {
                return PathValidationResult.Forbidden("Target path is a directory, not a document.");
            }

            return PathValidationResult.NotFound($"Document file does not exist on disk: '{candidatePath}'.");
        }

        // 8. Verify target is a file, not a directory
        var attr = File.GetAttributes(fullTarget);
        if (attr.HasFlag(FileAttributes.Directory))
        {
            return PathValidationResult.Forbidden("Target path is a directory, not a document.");
        }

        // 9. Symlink / Reparse Point Validation
        try
        {
            var fileInfo = new FileInfo(fullTarget);
            if (fileInfo.LinkTarget != null)
            {
                var resolvedTarget = fileInfo.ResolveLinkTarget(returnFinalTarget: true);
                if (resolvedTarget != null)
                {
                    string canonicalResolved = Path.GetFullPath(resolvedTarget.FullName);
                    if (!canonicalResolved.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        return PathValidationResult.Forbidden("Symlink targets an unauthorized path outside the vault.");
                    }
                }
            }
        }
        catch
        {
            return PathValidationResult.Forbidden("Unable to verify link target safety.");
        }

        return PathValidationResult.Success(fullTarget);
    }

    public static bool IsPathSafelyInsideRoot(string rootDirectory, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || string.IsNullOrWhiteSpace(candidatePath))
            return false;

        if (candidatePath.IndexOf('\0') >= 0 || candidatePath.Contains("%00", StringComparison.OrdinalIgnoreCase))
            return false;

        string decoded = candidatePath;
        for (int i = 0; i < MaxDecodingPasses; i++)
        {
            string next = WebUtility.UrlDecode(decoded);
            if (next == decoded) break;
            decoded = next;
        }

        if (decoded.IndexOf('\0') >= 0)
            return false;

        var fullRoot = Path.GetFullPath(rootDirectory.Replace('\\', '/'));
        if (!fullRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            fullRoot += Path.DirectorySeparatorChar;
        }

        var normalized = decoded.Replace('\\', '/');
        string fullTarget;
        if (Path.IsPathRooted(normalized))
        {
            fullTarget = Path.GetFullPath(normalized);
        }
        else
        {
            fullTarget = Path.GetFullPath(Path.Combine(fullRoot, normalized));
        }

        if (fullTarget.TrimEnd(Path.DirectorySeparatorChar).Equals(
            fullRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return fullTarget.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }
}

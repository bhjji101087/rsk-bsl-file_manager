using RSK.FileManager.Abstractions;

namespace RSK.FileManager.Core;

/// <summary>
/// Sanitizes and validates caller-supplied file paths to prevent traversal and
/// other path-based attacks. Used by every provider before any I/O, and again by
/// the File System serving endpoint.
/// </summary>
public static class PathSanitizer
{
    /// <summary>Maximum permitted path length, in characters.</summary>
    public const int MaxPathLength = 250;

    /// <summary>
    /// Validates the path (throwing <see cref="FileManagerValidationException"/> on any
    /// violation) and returns a normalized relative path using forward slashes, with
    /// leading slashes/dots stripped and duplicate slashes collapsed.
    /// </summary>
    public static string Sanitize(string path)
    {
        Validate(path);

        var normalized = path.Trim().Replace('\\', '/');

        // Strip any leading slashes or dots so the result is always relative.
        normalized = normalized.TrimStart('/', '.');

        // Collapse duplicate slashes (e.g. "a//b" -> "a/b").
        while (normalized.Contains("//"))
            normalized = normalized.Replace("//", "/");

        return normalized;
    }

    /// <summary>
    /// Validates the path without transforming it. Throws
    /// <see cref="FileManagerValidationException"/> if the path is unsafe.
    /// </summary>
    public static void Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new FileManagerValidationException("File path must not be null or empty.");

        var trimmed = path.Trim();

        if (trimmed.Length > MaxPathLength)
            throw new FileManagerValidationException(
                $"File path exceeds the maximum length of {MaxPathLength} characters.");

        if (trimmed.IndexOf('\0') >= 0)
            throw new FileManagerValidationException("File path contains a null byte.");

        foreach (var ch in trimmed)
        {
            if (char.IsControl(ch))
                throw new FileManagerValidationException("File path contains control characters.");
        }

        // ':' would allow Windows drive letters ("C:\...") or alternate data streams,
        // both of which can escape the storage root. Disallow it outright.
        if (trimmed.IndexOf(':') >= 0)
            throw new FileManagerValidationException(
                "File path must not contain ':' (drive letters / alternate data streams are not allowed).");

        // Reject ".." as a whole path segment (traversal), but allow it inside a name
        // such as "my..file.txt".
        var segments = trimmed.Replace('\\', '/').Split('/');
        foreach (var segment in segments)
        {
            if (segment == "..")
                throw new FileManagerValidationException(
                    "File path must not contain '..' traversal sequences.");
        }
    }
}

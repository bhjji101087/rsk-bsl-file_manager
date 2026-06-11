namespace RSK.FileManager.Abstractions;

/// <summary>File information retrieved without downloading the content.</summary>
public sealed class FileMetadata
{
    /// <summary>The file's stored path.</summary>
    public required string FilePath { get; init; }

    /// <summary>Size in bytes.</summary>
    public required long FileSizeBytes { get; init; }

    /// <summary>Content type.</summary>
    public required string ContentType { get; init; }

    /// <summary>Last modified timestamp.</summary>
    public required DateTimeOffset LastModified { get; init; }

    /// <summary>Creation timestamp, when available.</summary>
    public DateTimeOffset? CreatedOn { get; init; }

    /// <summary>Metadata stored alongside the file.</summary>
    public Dictionary<string, string> Metadata { get; init; } = new();
}

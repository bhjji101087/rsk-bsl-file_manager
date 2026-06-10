namespace RSK.FileManager.Abstractions;

/// <summary>Result of an upload operation.</summary>
public sealed class FileUploadResult
{
    /// <summary>The normalized path the file was stored under.</summary>
    public required string StoredPath { get; init; }

    /// <summary>Stored size in bytes.</summary>
    public required long FileSizeBytes { get; init; }

    /// <summary>Resolved content type.</summary>
    public required string ContentType { get; init; }

    /// <summary>When the upload completed.</summary>
    public required DateTimeOffset UploadedAt { get; init; }

    /// <summary>Secure URL, populated when GenerateUrlOnUpload is true.</summary>
    public string? SecureUrl { get; init; }

    /// <summary>Name of the provider that handled the upload.</summary>
    public string Provider { get; init; } = string.Empty;
}

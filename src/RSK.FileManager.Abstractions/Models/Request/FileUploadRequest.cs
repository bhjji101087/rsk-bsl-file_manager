namespace RSK.FileManager.Abstractions;

/// <summary>Input for an upload operation.</summary>
public sealed class FileUploadRequest
{
    /// <summary>Relative path including filename, e.g. "invoices/2026/06/inv001.pdf".</summary>
    public required string FilePath { get; init; }

    /// <summary>MIME type. If null, it is auto-detected from the extension.</summary>
    public string? ContentType { get; init; }

    /// <summary>Optional metadata key/value pairs stored alongside the file.</summary>
    public Dictionary<string, string>? Metadata { get; init; }

    /// <summary>If true and the file already exists, overwrite it. Default false (throws).</summary>
    public bool Overwrite { get; init; }
}

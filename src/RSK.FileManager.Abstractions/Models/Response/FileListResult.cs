namespace RSK.FileManager.Abstractions;

/// <summary>Result of listing the files in a folder.</summary>
public sealed class FileListResult
{
    /// <summary>The folder that was listed.</summary>
    public required string FolderPath { get; init; }

    /// <summary>The files found in the folder.</summary>
    public required IReadOnlyList<FileMetadata> Files { get; init; }
}

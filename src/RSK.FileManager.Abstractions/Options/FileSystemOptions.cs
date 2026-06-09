namespace RSK.FileManager.Abstractions;

/// <summary>File System provider configuration (the "FileManager:FileSystem" section).</summary>
public sealed class FileSystemOptions
{
    /// <summary>Root directory under which all files are stored.</summary>
    public string RootPath { get; init; } = string.Empty;

    /// <summary>
    /// Public base URL that the file-serving endpoint (MapFileManagerFileServer) is mounted on.
    /// Must never expose the physical <see cref="RootPath"/>.
    /// </summary>
    public string ServeBaseUrl { get; init; } = string.Empty;

    /// <summary>Secret used to sign HMAC secure-URL tokens. Minimum 32 characters.</summary>
    public string TokenSecret { get; init; } = string.Empty;

    /// <summary>
    /// When true, deleting a file also removes parent folders that become empty,
    /// walking up to <see cref="RootPath"/>. Default false.
    /// WARNING: not safe under concurrent writers (TOCTOU) — it can race with another
    /// process creating a file in the same folder. Enable only when single-writer.
    /// </summary>
    public bool RemoveEmptyFolders { get; init; }

    private const string PlaceholderPrefix = "REPLACE_WITH";
    private const int MinSecretLength = 32;

    /// <summary>
    /// Validates File System options. Throws <see cref="FileManagerConfigException"/>
    /// on any missing or weak value.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RootPath))
            throw new FileManagerConfigException("FileManager:FileSystem:RootPath is required.");

        if (string.IsNullOrWhiteSpace(ServeBaseUrl))
            throw new FileManagerConfigException("FileManager:FileSystem:ServeBaseUrl is required.");

        if (string.IsNullOrWhiteSpace(TokenSecret)
            || TokenSecret.Length < MinSecretLength
            || TokenSecret.StartsWith(PlaceholderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new FileManagerConfigException(
                "FileManager:FileSystem:TokenSecret must be set to a strong secret of at least " +
                $"{MinSecretLength} characters (and must not be the placeholder value).");
        }
    }
}

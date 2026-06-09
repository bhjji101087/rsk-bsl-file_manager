namespace RSK.FileManager.Abstractions;

/// <summary>
/// Root configuration for RSK.FileManager (bound from the "FileManager" section).
/// Call <see cref="Validate"/> at startup to fail fast on misconfiguration.
/// </summary>
public sealed class FileManagerOptions
{
    /// <summary>Which backend to use. Switching this (per environment) requires no code change.</summary>
    public StorageProvider Provider { get; init; }

    /// <summary>Azure container name / logical root for the File System.</summary>
    public string RootContainer { get; init; } = string.Empty;

    /// <summary>Maximum accepted upload size in bytes. Default 100 MB.</summary>
    public long MaxFileSizeBytes { get; init; } = 104_857_600;

    /// <summary>
    /// Default secure-URL lifetime, in hours.
    /// Greater than 0 = expires in N hours; 0 = never expires; negative = invalid (throws).
    /// Note: 0 (never) is rejected when the Azure provider uses Managed Identity (7-day SAS cap).
    /// </summary>
    public int DefaultSecureUrlExpiryHours { get; init; } = 1;

    /// <summary>When true, an upload also returns a secure URL in its result.</summary>
    public bool GenerateUrlOnUpload { get; init; } = true;

    /// <summary>Allowed file extensions (e.g. ".pdf"). Empty = allow any.</summary>
    public string[] AllowedExtensions { get; init; } = Array.Empty<string>();

    /// <summary>When true, validate file content by magic bytes (not just extension).</summary>
    public bool EnableContentSniffing { get; init; } = true;

    /// <summary>Azure Blob provider settings.</summary>
    public AzureBlobOptions AzureBlob { get; init; } = new();

    /// <summary>File System provider settings.</summary>
    public FileSystemOptions FileSystem { get; init; } = new();

    /// <summary>
    /// Validates the whole configuration tree. Throws <see cref="FileManagerConfigException"/>
    /// on the first problem found. Intended to be called once, at startup.
    /// </summary>
    public void Validate()
    {
        if (DefaultSecureUrlExpiryHours < 0)
            throw new FileManagerConfigException(
                "FileManager:DefaultSecureUrlExpiryHours cannot be negative " +
                "(use a positive number of hours, or 0 for never-expire).");

        if (MaxFileSizeBytes <= 0)
            throw new FileManagerConfigException(
                "FileManager:MaxFileSizeBytes must be greater than zero.");

        switch (Provider)
        {
            case StorageProvider.AzureBlob:
                AzureBlob.Validate(DefaultSecureUrlExpiryHours, RootContainer);
                break;

            case StorageProvider.FileSystem:
                FileSystem.Validate();
                break;

            default:
                throw new FileManagerConfigException($"Unknown FileManager:Provider value: {Provider}.");
        }
    }
}

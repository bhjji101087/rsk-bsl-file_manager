namespace RSK.FileManager.Abstractions;

/// <summary>
/// Unified file storage service for RSK applications.
/// Backed by Azure Blob Storage or the local File System based on configuration
/// ("FileManager:Provider"). The selected provider is transparent to the caller —
/// the same code runs against either backend, and switching environments is a
/// configuration change with no code change.
/// </summary>
public interface IFileManagerService
{
    // ── Upload ───────────────────────────────────────────────────────────────

    /// <summary>Uploads from a stream. Preferred for large files.</summary>
    Task<FileUploadResult> UploadAsync(
        FileUploadRequest request,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>Uploads from a byte array. Convenience overload.</summary>
    Task<FileUploadResult> UploadAsync(
        FileUploadRequest request,
        byte[] content,
        CancellationToken cancellationToken = default);

    // ── Download ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Downloads a file. The returned <see cref="FileDownloadResult"/> owns the
    /// content stream; the caller must dispose it. The result also carries content
    /// type, size and metadata, so a separate metadata call is not required.
    /// </summary>
    Task<FileDownloadResult> DownloadAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a time-limited secure URL for direct browser access.
    /// Azure → SAS URL (Service SAS or User Delegation SAS depending on auth mode).
    /// File System → HMAC-signed URL served by MapFileManagerFileServer().
    /// Expiry defaults to FileManager:DefaultSecureUrlExpiryHours unless overridden.
    /// </summary>
    /// <param name="filePath">The file to grant access to.</param>
    /// <param name="expiry">Optional per-call expiry override. Null uses the configured default.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<SecureUrlResult> GetSecureUrlAsync(
        string filePath,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);

    // ── Delete ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Deletes a file. The package does not enforce authorization — the caller must
    /// authorize the operation before invoking this.
    /// </summary>
    Task DeleteAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    // ── Query ────────────────────────────────────────────────────────────────

    /// <summary>Returns true if the file exists.</summary>
    Task<bool> ExistsAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>Gets file metadata without downloading the content.</summary>
    Task<FileMetadata> GetMetadataAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the files within a folder path.</summary>
    Task<FileListResult> ListAsync(
        string folderPath,
        CancellationToken cancellationToken = default);

    // ── Manage ───────────────────────────────────────────────────────────────

    /// <summary>Moves or renames a file. (Implemented in v1.1.)</summary>
    Task MoveAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);

    /// <summary>Copies a file to a new path. (Implemented in v1.1.)</summary>
    Task CopyAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);
}

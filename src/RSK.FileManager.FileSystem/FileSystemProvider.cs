using System.Linq;
using Microsoft.Extensions.Logging;
using RSK.FileManager.Abstractions;
using RSK.FileManager.Core;

namespace RSK.FileManager.FileSystem;

/// <summary>
/// Stores files on the local (or network/UNC) file system. Secure URLs are HMAC-signed
/// and served by MapFileManagerFileServer(). All resolved paths are verified to stay
/// under the configured root.
/// </summary>
public sealed class FileSystemProvider : FileManagerProviderBase, IFileManagerService
{
    private const string ProviderName = "FileSystem";
    private const int BufferSize = 81920;

    private readonly string _rootFull;
    private readonly string _serveBaseUrl;
    private readonly FileSystemTokenService _tokenService;
    private readonly bool _removeEmptyFolders;

    /// <summary>Creates the provider from configuration.</summary>
    public FileSystemProvider(FileManagerOptions options, ILogger<FileSystemProvider> logger)
        : base(options, logger)
    {
        var fs = options.FileSystem;
        _rootFull = Path.GetFullPath(fs.RootPath);
        _serveBaseUrl = fs.ServeBaseUrl.TrimEnd('/');
        _tokenService = new FileSystemTokenService(fs.TokenSecret);
        _removeEmptyFolders = fs.RemoveEmptyFolders;
    }

    /// <inheritdoc />
    public async Task<FileUploadResult> UploadAsync(FileUploadRequest request, Stream content, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new FileManagerValidationException("Upload request is null.");
        if (content is null) throw new FileManagerValidationException("Upload content is null.");

        Stream working = content;
        MemoryStream? buffered = null;

        if (!content.CanSeek)
        {
            buffered = new MemoryStream();
            await content.CopyToAsync(buffered, BufferSize, cancellationToken).ConfigureAwait(false);
            buffered.Position = 0;
            working = buffered;
        }

        try
        {
            var size = working.Length - working.Position;
            await ValidateUploadAsync(request.FilePath, size, working, cancellationToken).ConfigureAwait(false);

            var sanitized = NormalizePath(request.FilePath);
            var fullPath = ResolveFullPath(request.FilePath);

            if (File.Exists(fullPath) && !request.Overwrite)
                throw new FileManagerValidationException(
                    $"File already exists: '{sanitized}'. Set Overwrite = true to replace it.");

            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir!);

            var mode = request.Overwrite ? FileMode.Create : FileMode.CreateNew;
            using (var dest = new FileStream(fullPath, mode, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                await working.CopyToAsync(dest, BufferSize, cancellationToken).ConfigureAwait(false);
            }

            var contentType = ResolveContentType(request.FilePath, request.ContentType);

            string? url = null;
            if (Options.GenerateUrlOnUpload)
                url = (await GetSecureUrlAsync(sanitized, null, cancellationToken).ConfigureAwait(false)).Url;

            Logger.LogInformation(
                "[RSK.FileManager] Upload completed {FilePath} {FileSizeBytes}b via {Provider}",
                sanitized, size, ProviderName);

            return new FileUploadResult
            {
                StoredPath = sanitized,
                FileSizeBytes = size,
                ContentType = contentType,
                UploadedAt = DateTimeOffset.UtcNow,
                SecureUrl = url,
                Provider = ProviderName
            };
        }
        finally
        {
            buffered?.Dispose();
        }
    }

    /// <inheritdoc />
    public async Task<FileUploadResult> UploadAsync(FileUploadRequest request, byte[] content, CancellationToken cancellationToken = default)
    {
        if (content is null) throw new FileManagerValidationException("Upload content is null.");
        using var ms = new MemoryStream(content, writable: false);
        return await UploadAsync(request, ms, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<FileDownloadResult> DownloadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveFullPath(filePath);
        if (!File.Exists(fullPath))
            throw FileManagerNotFoundException.ForPath(NormalizePath(filePath));

        var info = new FileInfo(fullPath);
        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);

        var result = new FileDownloadResult
        {
            Content = stream,
            ContentType = ResolveContentType(filePath, null),
            FileSizeBytes = info.Length
        };

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<SecureUrlResult> GetSecureUrlAsync(string filePath, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var sanitized = NormalizePath(filePath);
        var expiresOn = ResolveExpiry(expiry);
        var token = _tokenService.Create(sanitized, expiresOn);

        var url = $"{_serveBaseUrl}/{EscapePath(sanitized)}";
        url += expiresOn is null
            ? $"?token={token}"
            : $"?expires={expiresOn.Value.ToUnixTimeSeconds()}&token={token}";

        return Task.FromResult(new SecureUrlResult
        {
            Url = url,
            ExpiresAt = expiresOn,
            FilePath = sanitized
        });
    }

    /// <inheritdoc />
    public Task DeleteAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveFullPath(filePath);
        if (!File.Exists(fullPath))
            throw FileManagerNotFoundException.ForPath(NormalizePath(filePath));

        File.Delete(fullPath);
        Logger.LogInformation("[RSK.FileManager] File deleted {FilePath}", NormalizePath(filePath));

        if (_removeEmptyFolders)
            RemoveEmptyParents(Path.GetDirectoryName(fullPath));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string filePath, CancellationToken cancellationToken = default)
        => Task.FromResult(File.Exists(ResolveFullPath(filePath)));

    /// <inheritdoc />
    public Task<FileMetadata> GetMetadataAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveFullPath(filePath);
        if (!File.Exists(fullPath))
            throw FileManagerNotFoundException.ForPath(NormalizePath(filePath));

        var info = new FileInfo(fullPath);
        return Task.FromResult(new FileMetadata
        {
            FilePath = NormalizePath(filePath),
            FileSizeBytes = info.Length,
            ContentType = ResolveContentType(filePath, null),
            LastModified = info.LastWriteTimeUtc,
            CreatedOn = info.CreationTimeUtc
        });
    }

    /// <inheritdoc />
    public Task<FileListResult> ListAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        string sanitized;
        string fullFolder;

        if (string.IsNullOrWhiteSpace(folderPath) || folderPath.Trim() == "/")
        {
            sanitized = string.Empty;
            fullFolder = _rootFull;
        }
        else
        {
            sanitized = NormalizePath(folderPath);
            fullFolder = ResolveFullPath(folderPath);
        }

        var files = new List<FileMetadata>();
        if (Directory.Exists(fullFolder))
        {
            foreach (var file in Directory.EnumerateFiles(fullFolder))
            {
                var info = new FileInfo(file);
                var rel = GetRelativePath(file);
                files.Add(new FileMetadata
                {
                    FilePath = rel,
                    FileSizeBytes = info.Length,
                    ContentType = ResolveContentType(rel, null),
                    LastModified = info.LastWriteTimeUtc,
                    CreatedOn = info.CreationTimeUtc
                });
            }
        }

        return Task.FromResult(new FileListResult { FolderPath = sanitized, Files = files });
    }

    /// <inheritdoc />
    public Task MoveAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        var src = ResolveFullPath(sourcePath);
        var dest = ResolveFullPath(destinationPath);

        if (!File.Exists(src))
            throw FileManagerNotFoundException.ForPath(NormalizePath(sourcePath));

        var destDir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir!);

        File.Move(src, dest);

        if (_removeEmptyFolders)
            RemoveEmptyParents(Path.GetDirectoryName(src));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        var src = ResolveFullPath(sourcePath);
        var dest = ResolveFullPath(destinationPath);

        if (!File.Exists(src))
            throw FileManagerNotFoundException.ForPath(NormalizePath(sourcePath));

        var destDir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir!);

        File.Copy(src, dest, overwrite: false);
        return Task.CompletedTask;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private string ResolveFullPath(string relative)
    {
        var sanitized = NormalizePath(relative);
        var combined = Path.GetFullPath(Path.Combine(_rootFull, sanitized));

        if (!IsUnderRoot(combined))
            throw new FileManagerValidationException("Resolved path escapes the storage root.");

        return combined;
    }

    private bool IsUnderRoot(string fullPath)
    {
        var rootWithSep = _rootFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? _rootFull
            : _rootFull + Path.DirectorySeparatorChar;

        return string.Equals(fullPath, _rootFull, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase);
    }

    private string GetRelativePath(string fullPath)
    {
        var full = Path.GetFullPath(fullPath);
        var rel = full.Substring(_rootFull.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return rel.Replace('\\', '/');
    }

    private void RemoveEmptyParents(string? directory)
    {
        var dir = directory;
        while (!string.IsNullOrEmpty(dir))
        {
            var full = Path.GetFullPath(dir!);
            if (string.Equals(full, _rootFull, StringComparison.OrdinalIgnoreCase)) break;
            if (!IsUnderRoot(full)) break;
            if (!Directory.Exists(full)) break;
            if (Directory.EnumerateFileSystemEntries(full).Any()) break;

            Directory.Delete(full);
            Logger.LogInformation("[RSK.FileManager] Empty folder removed {FolderPath}", full);
            dir = Path.GetDirectoryName(full);
        }
    }

    private static string EscapePath(string path)
        => string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
}

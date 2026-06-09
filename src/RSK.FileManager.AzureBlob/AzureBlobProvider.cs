using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Polly;
using RSK.FileManager.Abstractions;
using RSK.FileManager.Core;

namespace RSK.FileManager.AzureBlob;

/// <summary>
/// Stores files in Azure Blob Storage. A single container client is created once and
/// reused (it is thread-safe and owns the connection pool). Secure URLs are Service
/// SAS (connection string) or User Delegation SAS (Managed Identity).
/// </summary>
public sealed class AzureBlobProvider : FileManagerProviderBase, IFileManagerService
{
    private const string ProviderName = "AzureBlob";
    private const int BufferSize = 81920;

    private readonly BlobServiceClient _service;
    private readonly BlobContainerClient _container;
    private readonly bool _useManagedIdentity;
    private readonly string _accountName;
    private readonly ResiliencePipeline _retry;

    private readonly object _delegationLock = new();
    private UserDelegationKey? _delegationKey;
    private DateTimeOffset _delegationKeyExpiresOn;

    /// <summary>Creates the provider from configuration (single reused client).</summary>
    public AzureBlobProvider(FileManagerOptions options, ILogger<AzureBlobProvider> logger)
        : base(options, logger)
    {
        var az = options.AzureBlob;
        _useManagedIdentity = az.UseManagedIdentity;
        _accountName = az.AccountName;

        _service = az.UseManagedIdentity
            ? new BlobServiceClient(new Uri($"https://{az.AccountName}.blob.core.windows.net"), new DefaultAzureCredential())
            : new BlobServiceClient(az.ConnectionString);

        _container = _service.GetBlobContainerClient(options.RootContainer);
        _retry = RetryPolicies.Build(az.RetryMaxAttempts, az.RetryBaseDelaySeconds);
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

            var blobName = NormalizePath(request.FilePath);
            var blob = _container.GetBlobClient(blobName);

            if (!request.Overwrite)
            {
                var exists = await RunAsync(c => blob.ExistsAsync(c), blobName, cancellationToken).ConfigureAwait(false);
                if (exists.Value)
                    throw new FileManagerValidationException(
                        $"File already exists: '{blobName}'. Set Overwrite = true to replace it.");
            }

            var contentType = ResolveContentType(request.FilePath, request.ContentType);
            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                Conditions = request.Overwrite ? null : new BlobRequestConditions { IfNoneMatch = ETag.All }
            };
            if (request.Metadata is not null)
                uploadOptions.Metadata = request.Metadata;

            await RunAsync(c => blob.UploadAsync(working, uploadOptions, c), blobName, cancellationToken).ConfigureAwait(false);

            string? url = null;
            if (Options.GenerateUrlOnUpload)
                url = (await GetSecureUrlAsync(blobName, null, cancellationToken).ConfigureAwait(false)).Url;

            Logger.LogInformation(
                "[RSK.FileManager] Upload completed {FilePath} {FileSizeBytes}b via {Provider}",
                blobName, size, ProviderName);

            return new FileUploadResult
            {
                StoredPath = blobName,
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
    public async Task<FileDownloadResult> DownloadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var blobName = NormalizePath(filePath);
        var blob = _container.GetBlobClient(blobName);

        var response = await RunAsync(c => blob.DownloadStreamingAsync(cancellationToken: c), blobName, cancellationToken).ConfigureAwait(false);
        var details = response.Value.Details;

        return new FileDownloadResult
        {
            Content = response.Value.Content,
            ContentType = string.IsNullOrEmpty(details.ContentType) ? ResolveContentType(filePath, null) : details.ContentType,
            FileSizeBytes = details.ContentLength,
            Metadata = details.Metadata is null ? new Dictionary<string, string>() : new Dictionary<string, string>(details.Metadata)
        };
    }

    /// <inheritdoc />
    public async Task<SecureUrlResult> GetSecureUrlAsync(string filePath, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var blobName = NormalizePath(filePath);
        var expiresOn = ResolveExpiry(expiry);

        var url = _useManagedIdentity
            ? await GenerateUserDelegationSasAsync(blobName, expiresOn, cancellationToken).ConfigureAwait(false)
            : GenerateServiceSas(blobName, expiresOn);

        return new SecureUrlResult { Url = url, ExpiresAt = expiresOn, FilePath = blobName };
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var blobName = NormalizePath(filePath);
        var blob = _container.GetBlobClient(blobName);

        var resp = await RunAsync(
            c => blob.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: c),
            blobName, cancellationToken).ConfigureAwait(false);

        if (!resp.Value)
            throw FileManagerNotFoundException.ForPath(blobName);

        // Azure's flat namespace means empty "folders" (prefixes) cease to exist automatically.
        Logger.LogInformation("[RSK.FileManager] File deleted {FilePath}", blobName);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var blobName = NormalizePath(filePath);
        var blob = _container.GetBlobClient(blobName);
        var resp = await RunAsync(c => blob.ExistsAsync(c), blobName, cancellationToken).ConfigureAwait(false);
        return resp.Value;
    }

    /// <inheritdoc />
    public async Task<FileMetadata> GetMetadataAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var blobName = NormalizePath(filePath);
        var blob = _container.GetBlobClient(blobName);
        var resp = await RunAsync(c => blob.GetPropertiesAsync(cancellationToken: c), blobName, cancellationToken).ConfigureAwait(false);
        var p = resp.Value;

        return new FileMetadata
        {
            FilePath = blobName,
            FileSizeBytes = p.ContentLength,
            ContentType = string.IsNullOrEmpty(p.ContentType) ? ResolveContentType(filePath, null) : p.ContentType,
            LastModified = p.LastModified,
            CreatedOn = p.CreatedOn,
            Metadata = p.Metadata is null ? new Dictionary<string, string>() : new Dictionary<string, string>(p.Metadata)
        };
    }

    /// <inheritdoc />
    public async Task<FileListResult> ListAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        var prefix = string.IsNullOrWhiteSpace(folderPath)
            ? string.Empty
            : NormalizePath(folderPath).TrimEnd('/') + "/";

        var files = new List<FileMetadata>();
        await foreach (var item in _container
            .GetBlobsAsync(traits: BlobTraits.Metadata, prefix: prefix, cancellationToken: cancellationToken)
            .ConfigureAwait(false))
        {
            files.Add(new FileMetadata
            {
                FilePath = item.Name,
                FileSizeBytes = item.Properties.ContentLength ?? 0,
                ContentType = string.IsNullOrEmpty(item.Properties.ContentType)
                    ? ResolveContentType(item.Name, null)
                    : item.Properties.ContentType!,
                LastModified = item.Properties.LastModified ?? DateTimeOffset.MinValue,
                CreatedOn = item.Properties.CreatedOn,
                Metadata = item.Metadata is null ? new Dictionary<string, string>() : new Dictionary<string, string>(item.Metadata)
            });
        }

        return new FileListResult { FolderPath = prefix.TrimEnd('/'), Files = files };
    }

    /// <inheritdoc />
    public Task MoveAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Move for the Azure Blob provider is planned for v1.1.");

    /// <inheritdoc />
    public Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Copy for the Azure Blob provider is planned for v1.1.");

    // ── SAS generation ───────────────────────────────────────────────────────

    private string GenerateServiceSas(string blobName, DateTimeOffset? expiresOn)
    {
        var blob = _container.GetBlobClient(blobName);
        if (!blob.CanGenerateSasUri)
            throw new FileManagerProviderException(
                "Cannot generate a Service SAS: the client is not authenticated with a shared key. " +
                "Use a connection string, or enable Managed Identity for a User Delegation SAS.");

        var builder = new BlobSasBuilder
        {
            BlobContainerName = _container.Name,
            BlobName = blobName,
            Resource = "b",
            ExpiresOn = expiresOn ?? DateTimeOffset.MaxValue
        };
        builder.SetPermissions(BlobSasPermissions.Read);

        return blob.GenerateSasUri(builder).ToString();
    }

    private async Task<string> GenerateUserDelegationSasAsync(string blobName, DateTimeOffset? expiresOn, CancellationToken cancellationToken)
    {
        // Never-expire under Managed Identity is rejected at config validation, so
        // expiresOn is always bounded (<= 7 days) here.
        var exp = expiresOn ?? DateTimeOffset.UtcNow.AddHours(1);
        var key = await GetUserDelegationKeyAsync(exp, cancellationToken).ConfigureAwait(false);

        var builder = new BlobSasBuilder
        {
            BlobContainerName = _container.Name,
            BlobName = blobName,
            Resource = "b",
            ExpiresOn = exp
        };
        builder.SetPermissions(BlobSasPermissions.Read);

        var sas = builder.ToSasQueryParameters(key, _accountName).ToString();
        var blob = _container.GetBlobClient(blobName);
        return $"{blob.Uri}?{sas}";
    }

    private async Task<UserDelegationKey> GetUserDelegationKeyAsync(DateTimeOffset neededUntil, CancellationToken cancellationToken)
    {
        lock (_delegationLock)
        {
            if (_delegationKey is not null && _delegationKeyExpiresOn >= neededUntil)
                return _delegationKey;
        }

        // Request a key valid for the maximum 7-day window and cache it.
        var start = DateTimeOffset.UtcNow.AddMinutes(-5);
        var keyExpiry = DateTimeOffset.UtcNow.AddDays(7);
        var resp = await _service.GetUserDelegationKeyAsync(start, keyExpiry, cancellationToken).ConfigureAwait(false);

        lock (_delegationLock)
        {
            _delegationKey = resp.Value;
            _delegationKeyExpiresOn = keyExpiry;
        }

        Logger.LogDebug("[RSK.FileManager] User delegation key refreshed, expires {ExpiresOn}", keyExpiry);
        return resp.Value;
    }

    // ── Retry + error mapping ────────────────────────────────────────────────

    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, string blobName, CancellationToken cancellationToken)
    {
        try
        {
            return await _retry
                .ExecuteAsync(async token => await action(token).ConfigureAwait(false), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RequestFailedException ex)
        {
            throw Map(ex, blobName);
        }
    }

    private static Exception Map(RequestFailedException ex, string blobName) => ex.Status switch
    {
        404 => FileManagerNotFoundException.ForPath(blobName),
        429 or 500 or 503 => new FileManagerTransientException($"Azure storage transient failure (HTTP {ex.Status}).", ex),
        _ => new FileManagerProviderException($"Azure storage failure (HTTP {ex.Status}).", ex)
    };
}

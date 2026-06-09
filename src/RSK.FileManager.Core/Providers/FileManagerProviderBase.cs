using Microsoft.Extensions.Logging;
using RSK.FileManager.Abstractions;

namespace RSK.FileManager.Core;

/// <summary>
/// Shared base for storage providers: holds options and logger, runs the common
/// upload validation pipeline, resolves the secure-URL expiry rule, and maps
/// extensions to content types.
/// </summary>
public abstract class FileManagerProviderBase
{
    /// <summary>The resolved configuration.</summary>
    protected FileManagerOptions Options { get; }

    /// <summary>The provider logger.</summary>
    protected ILogger Logger { get; }

    /// <summary>Initializes the base with options and a logger.</summary>
    protected FileManagerProviderBase(FileManagerOptions options, ILogger logger)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Sanitizes a caller path to a normalized relative path.</summary>
    protected static string NormalizePath(string path) => PathSanitizer.Sanitize(path);

    /// <summary>
    /// Runs the shared pre-upload checks: path, size, extension whitelist, and
    /// (when enabled) magic-byte content sniffing.
    /// </summary>
    protected async Task ValidateUploadAsync(string path, long sizeBytes, Stream content, CancellationToken cancellationToken)
    {
        PathSanitizer.Validate(path);
        FileValidator.ValidateSize(sizeBytes, Options);
        FileValidator.ValidateExtension(path, Options);

        if (Options.EnableContentSniffing)
            await FileValidator.ValidateContentAsync(content, path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the absolute expiry instant for a secure URL.
    /// A per-call value overrides the configured default; a non-positive duration,
    /// or a configured value of 0, means "never expires" (returns null).
    /// </summary>
    protected DateTimeOffset? ResolveExpiry(TimeSpan? perCall)
    {
        if (perCall.HasValue)
            return perCall.Value <= TimeSpan.Zero ? null : DateTimeOffset.UtcNow.Add(perCall.Value);

        var hours = Options.DefaultSecureUrlExpiryHours;
        return hours == 0 ? null : DateTimeOffset.UtcNow.AddHours(hours);
    }

    /// <summary>Returns the provided content type, or one inferred from the extension.</summary>
    protected static string ResolveContentType(string path, string? provided)
    {
        if (!string.IsNullOrWhiteSpace(provided))
            return provided!;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".zip" => "application/zip",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            _ => "application/octet-stream"
        };
    }
}

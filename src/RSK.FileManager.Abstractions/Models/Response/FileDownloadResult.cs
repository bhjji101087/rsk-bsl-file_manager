namespace RSK.FileManager.Abstractions;

/// <summary>
/// Result of a download. Owns the content stream — the caller must dispose it.
/// Carries content type, size and metadata received in the same provider call, so
/// no separate GetMetadataAsync round-trip is needed.
/// </summary>
/// <remarks>
/// On modern .NET (net6.0+/netstandard2.1+) this also implements
/// <see cref="IAsyncDisposable"/> (use <c>await using</c>). On net462/netstandard2.0,
/// where <c>Stream.DisposeAsync</c> does not exist, only <see cref="IDisposable"/> is
/// available (use <c>using</c>).
/// </remarks>
public sealed class FileDownloadResult :
#if NET6_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
    IAsyncDisposable,
#endif
    IDisposable
{
    /// <summary>The readable content stream. Disposed when this result is disposed.</summary>
    public required Stream Content { get; init; }

    /// <summary>Content type of the file.</summary>
    public required string ContentType { get; init; }

    /// <summary>Size of the file in bytes.</summary>
    public required long FileSizeBytes { get; init; }

    /// <summary>Metadata stored alongside the file.</summary>
    public Dictionary<string, string> Metadata { get; init; } = new();

#if NET6_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
    /// <summary>Disposes the underlying content stream asynchronously.</summary>
    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return Content.DisposeAsync();
    }
#endif

    /// <summary>Disposes the underlying content stream.</summary>
    public void Dispose()
    {
        Content.Dispose();
        GC.SuppressFinalize(this);
    }
}

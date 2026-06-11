namespace RSK.FileManager.Abstractions;

/// <summary>A time-limited secure URL for direct browser access to a file.</summary>
public sealed class SecureUrlResult
{
    /// <summary>The secure URL.</summary>
    public required string Url { get; init; }

    /// <summary>When the URL expires. Null when the URL never expires.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>The file the URL points to.</summary>
    public required string FilePath { get; init; }
}

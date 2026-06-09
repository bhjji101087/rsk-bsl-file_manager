namespace RSK.FileManager.Abstractions;

/// <summary>
/// Thrown when the underlying storage backend (Azure Blob or File System) fails.
/// Maps naturally to an HTTP 503 in a consuming API.
/// </summary>
[Serializable]
public class FileManagerProviderException : FileManagerException
{
    /// <summary>Initializes a new instance.</summary>
    public FileManagerProviderException() { }

    /// <summary>Initializes a new instance with a message.</summary>
    public FileManagerProviderException(string message) : base(message) { }

    /// <summary>Initializes a new instance with a message and inner exception.</summary>
    public FileManagerProviderException(string message, Exception innerException)
        : base(message, innerException) { }
}

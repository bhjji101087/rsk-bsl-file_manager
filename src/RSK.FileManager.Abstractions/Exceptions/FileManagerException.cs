namespace RSK.FileManager.Abstractions;

/// <summary>
/// Base type for all exceptions raised by RSK.FileManager.
/// Consumers can catch this single type to handle any vault error.
/// </summary>
[Serializable]
public class FileManagerException : Exception
{
    /// <summary>Initializes a new instance.</summary>
    public FileManagerException() { }

    /// <summary>Initializes a new instance with a message.</summary>
    public FileManagerException(string message) : base(message) { }

    /// <summary>Initializes a new instance with a message and inner exception.</summary>
    public FileManagerException(string message, Exception innerException)
        : base(message, innerException) { }
}

namespace RSK.FileManager.Abstractions;

/// <summary>Thrown when a requested file or folder does not exist.</summary>
[Serializable]
public sealed class FileManagerNotFoundException : FileManagerException
{
    /// <summary>Initializes a new instance.</summary>
    public FileManagerNotFoundException() { }

    /// <summary>Initializes a new instance with a message.</summary>
    public FileManagerNotFoundException(string message) : base(message) { }

    /// <summary>Initializes a new instance with a message and inner exception.</summary>
    public FileManagerNotFoundException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>Creates an exception for a missing path.</summary>
    public static FileManagerNotFoundException ForPath(string filePath)
        => new($"File or folder not found: '{filePath}'.");
}

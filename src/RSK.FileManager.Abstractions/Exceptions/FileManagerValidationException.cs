namespace RSK.FileManager.Abstractions;

/// <summary>
/// Thrown when caller input is invalid: a bad path, an oversize file,
/// a disallowed extension, or content that fails sniffing.
/// Maps naturally to an HTTP 400 in a consuming API.
/// </summary>
[Serializable]
public sealed class FileManagerValidationException : FileManagerException
{
    /// <summary>Initializes a new instance.</summary>
    public FileManagerValidationException() { }

    /// <summary>Initializes a new instance with a message.</summary>
    public FileManagerValidationException(string message) : base(message) { }

    /// <summary>Initializes a new instance with a message and inner exception.</summary>
    public FileManagerValidationException(string message, Exception innerException)
        : base(message, innerException) { }
}

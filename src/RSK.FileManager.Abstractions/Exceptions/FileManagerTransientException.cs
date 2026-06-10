namespace RSK.FileManager.Abstractions;

/// <summary>
/// A temporary storage failure that is safe to retry. Surfaced after the
/// provider's own retry policy (Polly) has been exhausted.
/// </summary>
[Serializable]
public sealed class FileManagerTransientException : FileManagerProviderException
{
    /// <summary>Initializes a new instance.</summary>
    public FileManagerTransientException() { }

    /// <summary>Initializes a new instance with a message.</summary>
    public FileManagerTransientException(string message) : base(message) { }

    /// <summary>Initializes a new instance with a message and inner exception.</summary>
    public FileManagerTransientException(string message, Exception innerException)
        : base(message, innerException) { }
}

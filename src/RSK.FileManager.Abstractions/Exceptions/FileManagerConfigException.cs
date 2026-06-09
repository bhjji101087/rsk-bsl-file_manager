namespace RSK.FileManager.Abstractions;

/// <summary>
/// Thrown at startup when configuration is missing or invalid.
/// Part of the fail-fast contract: misconfiguration surfaces during
/// registration, never at the first runtime call.
/// </summary>
[Serializable]
public sealed class FileManagerConfigException : FileManagerException
{
    /// <summary>Initializes a new instance.</summary>
    public FileManagerConfigException() { }

    /// <summary>Initializes a new instance with a message.</summary>
    public FileManagerConfigException(string message) : base(message) { }

    /// <summary>Initializes a new instance with a message and inner exception.</summary>
    public FileManagerConfigException(string message, Exception innerException)
        : base(message, innerException) { }
}

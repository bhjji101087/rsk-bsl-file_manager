namespace RSK.FileManager.Abstractions;

/// <summary>Selects which storage backend RSK.FileManager uses.</summary>
public enum StorageProvider
{
    /// <summary>Azure Blob Storage.</summary>
    AzureBlob = 0,

    /// <summary>Local (or network/UNC) file system.</summary>
    FileSystem = 1
}

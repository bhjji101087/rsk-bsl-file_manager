namespace RSK.FileManager.Abstractions;

/// <summary>Azure Blob Storage provider configuration (the "FileManager:AzureBlob" section).</summary>
public sealed class AzureBlobOptions
{
    /// <summary>
    /// When true, authenticate with Managed Identity (no secret in config).
    /// This forces the User Delegation SAS code path, which Azure caps at 7 days.
    /// </summary>
    public bool UseManagedIdentity { get; init; }

    /// <summary>Storage account name. Required when <see cref="UseManagedIdentity"/> is true.</summary>
    public string AccountName { get; init; } = string.Empty;

    /// <summary>Connection string. Required when <see cref="UseManagedIdentity"/> is false.</summary>
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>Maximum Polly retry attempts for transient failures.</summary>
    public int RetryMaxAttempts { get; init; } = 3;

    /// <summary>Base delay (seconds) for exponential backoff: 2s, 4s, 8s, ...</summary>
    public int RetryBaseDelaySeconds { get; init; } = 2;

    /// <summary>
    /// Validates Azure options in the context of the root expiry setting and container.
    /// Throws <see cref="FileManagerConfigException"/> on any invalid combination.
    /// </summary>
    /// <param name="expiryHours">The root DefaultSecureUrlExpiryHours value.</param>
    /// <param name="rootContainer">The root RootContainer value.</param>
    public void Validate(int expiryHours, string rootContainer)
    {
        if (string.IsNullOrWhiteSpace(rootContainer))
            throw new FileManagerConfigException(
                "FileManager:RootContainer is required for the AzureBlob provider.");

        if (RetryMaxAttempts < 0)
            throw new FileManagerConfigException(
                "FileManager:AzureBlob:RetryMaxAttempts cannot be negative.");

        if (RetryBaseDelaySeconds < 0)
            throw new FileManagerConfigException(
                "FileManager:AzureBlob:RetryBaseDelaySeconds cannot be negative.");

        if (UseManagedIdentity)
        {
            if (string.IsNullOrWhiteSpace(AccountName))
                throw new FileManagerConfigException(
                    "FileManager:AzureBlob:AccountName is required when UseManagedIdentity = true.");

            // User Delegation SAS lifetime is bounded by the user delegation key (max 7 days);
            // a never-expiring URL (expiry = 0) is therefore impossible with Managed Identity.
            if (expiryHours == 0)
                throw new FileManagerConfigException(
                    "Never-expiring URLs (DefaultSecureUrlExpiryHours = 0) are not supported with " +
                    "Managed Identity. Azure caps a User Delegation SAS at 7 days (168 hours). " +
                    "Set a value between 1 and 168, or use a connection string.");

            if (expiryHours > 168)
                throw new FileManagerConfigException(
                    "With Managed Identity, DefaultSecureUrlExpiryHours cannot exceed 168 (7 days).");
        }
        else if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new FileManagerConfigException(
                "FileManager:AzureBlob:ConnectionString is required when UseManagedIdentity = false.");
        }
    }
}

using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using RSK.FileManager.Abstractions;
using RSK.FileManager.Extensions;

namespace RSK.FileManager;

/// <summary>
/// Manual registration for environments without dependency injection (e.g. classic
/// ASP.NET on .NET Framework 4.6.x). Call <see cref="Initialize"/> once at startup,
/// then resolve the cached singleton with <see cref="GetService"/>.
/// </summary>
public static class FileManagerFactory
{
    private static readonly object Lock = new();
    private static IFileManagerService? _instance;

    /// <summary>
    /// Builds and validates options from flat configuration keys (e.g. web.config
    /// &lt;appSettings&gt; with "FileManager:*" keys), constructs the configured
    /// provider, and caches it for the process lifetime.
    /// </summary>
    /// <param name="appSettings">The flat key/value settings.</param>
    /// <param name="prefix">Key prefix. Defaults to "FileManager".</param>
    public static void Initialize(NameValueCollection appSettings, string prefix = "FileManager")
    {
        if (appSettings is null) throw new ArgumentNullException(nameof(appSettings));

        var options = BuildOptions(appSettings, prefix);
        options.Validate();   // fail fast

        lock (Lock)
        {
            _instance = ServiceCollectionExtensions.CreateProvider(options, NullLoggerFactory.Instance);
        }
    }

    /// <summary>Returns the singleton service. Throws if <see cref="Initialize"/> was not called.</summary>
    public static IFileManagerService GetService()
    {
        lock (Lock)
        {
            return _instance
                ?? throw new FileManagerConfigException(
                    "FileManagerFactory.Initialize(...) must be called before GetService().");
        }
    }

    private static FileManagerOptions BuildOptions(NameValueCollection s, string prefix)
    {
        string? Get(string key) => s[$"{prefix}:{key}"];
        bool Bool(string key, bool fallback = false) => bool.TryParse(Get(key), out var v) ? v : fallback;
        int Int(string key, int fallback) =>
            int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        long Long(string key, long fallback) =>
            long.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        Enum.TryParse<StorageProvider>(Get("Provider") ?? nameof(StorageProvider.FileSystem), ignoreCase: true, out var provider);

        var allowedRaw = Get("AllowedExtensions");
        var allowed = string.IsNullOrWhiteSpace(allowedRaw)
            ? Array.Empty<string>()
            : allowedRaw!.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(x => x.Trim())
                         .ToArray();

        return new FileManagerOptions
        {
            Provider = provider,
            RootContainer = Get("RootContainer") ?? string.Empty,
            MaxFileSizeBytes = Long("MaxFileSizeBytes", 104_857_600),
            DefaultSecureUrlExpiryHours = Int("DefaultSecureUrlExpiryHours", 1),
            GenerateUrlOnUpload = Bool("GenerateUrlOnUpload", true),
            EnableContentSniffing = Bool("EnableContentSniffing", true),
            AllowedExtensions = allowed,
            AzureBlob = new AzureBlobOptions
            {
                UseManagedIdentity = Bool("AzureBlob:UseManagedIdentity"),
                AccountName = Get("AzureBlob:AccountName") ?? string.Empty,
                ConnectionString = Get("AzureBlob:ConnectionString") ?? string.Empty,
                RetryMaxAttempts = Int("AzureBlob:RetryMaxAttempts", 3),
                RetryBaseDelaySeconds = Int("AzureBlob:RetryBaseDelaySeconds", 2)
            },
            FileSystem = new FileSystemOptions
            {
                RootPath = Get("FileSystem:RootPath") ?? string.Empty,
                ServeBaseUrl = Get("FileSystem:ServeBaseUrl") ?? string.Empty,
                TokenSecret = Get("FileSystem:TokenSecret") ?? string.Empty,
                RemoveEmptyFolders = Bool("FileSystem:RemoveEmptyFolders")
            }
        };
    }
}

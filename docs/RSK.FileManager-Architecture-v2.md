# RSK.FileManager — Solution Architecture (Revised)

**Author:** Solution Architecture
**Company:** RSK
**Document Version:** 2.0 (supersedes 1.0 *RSK.FileVault*)
**Date:** June 2026
**Status:** Approved for Phase 1 development

> **Changes vs v1.0.** Renamed `RSK.FileVault` → `RSK.FileManager`. Split into multiple NuGet packages. Corrected secure-URL design (Service SAS vs User Delegation SAS), formalised the expiry rule, hardened HMAC tokens, mandated `ConfigureAwait(false)`, changed `DownloadAsync` to return a lose-nothing disposable result, pulled magic-byte content sniffing into v1.0, made empty-folder cleanup opt-in, added PolySharp polyfills, switched to plain Polly, and reused a singleton storage client. A full change log is in §25.

---

## 1. Executive Summary

RSK.FileManager is an enterprise-grade file-storage abstraction for all RSK applications. Consuming apps depend only on a single interface, `IFileManagerService`, backed by either **Azure Blob Storage** or the **Local File System**, selected entirely by configuration. The same compiled application runs against the File System in development and Azure Blob in UAT/Production with **zero code changes** — only `appsettings` differs.

It is delivered as a small family of NuGet packages so that an app pulls in only what it uses: the contract is dependency-light, and the Azure SDK is only present when the Azure provider is referenced.

---

## 2. Project Identity

| Property | Value |
|---|---|
| Product Name | RSK.FileManager |
| Namespace Root | `RSK.FileManager` |
| Primary Interface | `IFileManagerService` |
| Config Section | `FileManager` |
| NuGet Feed | RSK Internal Azure Artifacts |
| License | Internal Proprietary |
| Target Frameworks | `net462`, `netstandard2.0`, `net6.0`, `net8.0` |
| Language | C# 12 (multi-TFM via PolySharp polyfills) |
| Azure SDK | `Azure.Storage.Blobs` 12.x |
| Resilience | `Polly` 8.x (the lean package, **not** Microsoft.Extensions.Resilience) |

---

## 3. Architecture Principles

1. **Single Responsibility** — each class does one thing.
2. **Open/Closed** — new providers are added as new packages without touching existing code.
3. **Dependency Inversion** — apps depend on `RSK.FileManager.Abstractions`, never on a concrete provider.
4. **Configuration over Code** — provider, limits and security settings are config-driven; switching environments is a config change.
5. **Fail Fast** — configuration is validated at startup, not at first call.
6. **Defense in Depth** — validation at every layer: input → path → size → extension → **content (magic bytes)** → output.
7. **Observability First** — structured logging on every operation.
8. **Async by Default, Deadlock-Safe by Construction** — all I/O is async with `CancellationToken`, and the library uses `ConfigureAwait(false)` on every internal await so it is safe to call from classic ASP.NET. (See §14.)

---

## 4. Package Architecture

The solution ships as a package family rather than one assembly. This makes Open/Closed real, keeps the contract dependency-light, and prevents an Azure SDK upgrade from forcing a major version bump on the abstraction.

```
RSK.FileManager.Abstractions      ← interface, models, options, exceptions. Apps reference THIS.
        ▲           ▲
        │           │
RSK.FileManager.AzureBlob   RSK.FileManager.FileSystem
        ▲           ▲           ▲
        │           │           │
RSK.FileManager (registration glue: AddFileManager + FileManagerFactory; references both providers)
        ▲
        │
RSK.FileManager.AspNetCore  ← MapFileManagerFileServer() endpoint + health checks (net6.0/net8.0)
```

| Package | TFMs | Responsibility | Key dependencies |
|---|---|---|---|
| `RSK.FileManager.Abstractions` | net462; netstandard2.0; net6.0; net8.0 | `IFileManagerService`, request/response models, `FileManagerOptions`, exception hierarchy | `Microsoft.Extensions.Logging.Abstractions` only |
| `RSK.FileManager.AzureBlob` | net462; netstandard2.0; net6.0; net8.0 | `AzureBlobProvider`, SAS generation, Polly retry | `Azure.Storage.Blobs`, `Polly` |
| `RSK.FileManager.FileSystem` | net462; netstandard2.0; net6.0; net8.0 | `FileSystemProvider`, HMAC token service | none beyond Abstractions |
| `RSK.FileManager` | net462; netstandard2.0; net6.0; net8.0 | `AddFileManager()` (Core DI) + `FileManagerFactory` (net462); selects provider by config; references both providers | the two providers |
| `RSK.FileManager.AspNetCore` | net6.0; net8.0 | `MapFileManagerFileServer()` file-serving endpoint, `AddFileManagerHealthCheck()` | ASP.NET Core |

**Reference matrix for consumers**

- *Standard app that switches FS↔Blob by environment* (the primary scenario): reference `RSK.FileManager` (+ `RSK.FileManager.AspNetCore` for the file-serving endpoint). Both providers are present; config picks one.
- *Pure net462, FileSystem-only, wanting to avoid the Azure SDK / binding-redirect tail*: reference `RSK.FileManager.Abstractions` + `RSK.FileManager.FileSystem` and register via `FileManagerFactory` directly. No Azure assemblies are pulled in.

---

## 5. Solution Structure

```
RSK.FileManager/
│
├── src/
│   ├── RSK.FileManager.Abstractions/
│   │   ├── IFileManagerService.cs
│   │   ├── Models/
│   │   │   ├── Request/
│   │   │   │   └── FileUploadRequest.cs
│   │   │   └── Response/
│   │   │       ├── FileUploadResult.cs
│   │   │       ├── FileDownloadResult.cs        ← stream + metadata, IAsyncDisposable
│   │   │       ├── FileMetadata.cs
│   │   │       ├── FileListResult.cs
│   │   │       └── SecureUrlResult.cs
│   │   ├── Options/
│   │   │   ├── FileManagerOptions.cs
│   │   │   ├── AzureBlobOptions.cs
│   │   │   ├── FileSystemOptions.cs
│   │   │   └── StorageProvider.cs               ← enum
│   │   ├── Exceptions/
│   │   │   ├── FileManagerException.cs
│   │   │   ├── FileManagerNotFoundException.cs
│   │   │   ├── FileManagerValidationException.cs
│   │   │   ├── FileManagerProviderException.cs
│   │   │   ├── FileManagerTransientException.cs
│   │   │   └── FileManagerConfigException.cs
│   │   ├── Polyfills.cs (or PolySharp package ref)
│   │   └── RSK.FileManager.Abstractions.csproj
│   │
│   ├── RSK.FileManager.AzureBlob/
│   │   ├── AzureBlobProvider.cs
│   │   ├── Internal/RetryPolicies.cs            ← Polly
│   │   └── RSK.FileManager.AzureBlob.csproj
│   │
│   ├── RSK.FileManager.FileSystem/
│   │   ├── FileSystemProvider.cs
│   │   ├── Security/FileSystemTokenService.cs   ← HMAC signed URLs
│   │   └── RSK.FileManager.FileSystem.csproj
│   │
│   ├── RSK.FileManager/                          ← registration glue
│   │   ├── Providers/Base/FileManagerProviderBase.cs
│   │   ├── Security/PathSanitizer.cs
│   │   ├── Security/FileValidator.cs            ← extension + size + magic-byte sniffing
│   │   ├── Extensions/ServiceCollectionExtensions.cs   ← AddFileManager
│   │   ├── Legacy/FileManagerFactory.cs         ← net462 manual factory
│   │   └── RSK.FileManager.csproj
│   │
│   └── RSK.FileManager.AspNetCore/
│       ├── FileManagerEndpointExtensions.cs     ← MapFileManagerFileServer
│       ├── FileManagerHealthCheck.cs
│       └── RSK.FileManager.AspNetCore.csproj
│
├── tests/
│   ├── RSK.FileManager.UnitTests/
│   ├── RSK.FileManager.IntegrationTests/        ← Azurite + temp FS
│   └── RSK.FileManager.CompatTests/             ← net462
│
├── samples/
│   ├── RSK.FileManager.Sample.NetCore/
│   └── RSK.FileManager.Sample.NetFramework/
│
├── build/
│   └── Directory.Build.props                    ← shared TFMs, Nullable, analyzers, PolySharp
├── docs/ (CHANGELOG.md, CONFIGURATION.md, MIGRATION.md)
├── .github/workflows/ (ci.yml, publish.yml)
├── RSK.FileManager.sln
└── README.md
```

> **Note.** `PathSanitizer`, `FileValidator` and `FileManagerProviderBase` live in the glue package `RSK.FileManager` (shared by both providers) rather than in Abstractions, to keep Abstractions free of logic. If you prefer the providers to be usable without the glue package, move these into a tiny `RSK.FileManager.Core` shared package that both providers reference. Either is acceptable; the plan assumes the former.

---

## 6. Interface Contract

```csharp
namespace RSK.FileManager.Abstractions
{
    /// <summary>
    /// Unified file storage service for RSK applications.
    /// Backed by Azure Blob Storage or the File System based on configuration.
    /// The selected provider is transparent to the caller — the same code runs
    /// against either backend, chosen by the "FileManager:Provider" config value.
    /// </summary>
    public interface IFileManagerService
    {
        // ── Upload ───────────────────────────────────────────────────────────
        Task<FileUploadResult> UploadAsync(
            FileUploadRequest request, Stream content,
            CancellationToken cancellationToken = default);

        Task<FileUploadResult> UploadAsync(
            FileUploadRequest request, byte[] content,
            CancellationToken cancellationToken = default);

        // ── Download ─────────────────────────────────────────────────────────
        /// <summary>
        /// Download a file. The returned <see cref="FileDownloadResult"/> owns the
        /// stream; the CALLER must dispose it (use 'await using'). The result also
        /// carries content type, size and metadata so no second round-trip is needed.
        /// </summary>
        Task<FileDownloadResult> DownloadAsync(
            string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Time-limited secure URL for direct browser access.
        /// Azure → SAS URL (Service SAS or User Delegation SAS per auth mode).
        /// FileSystem → HMAC-signed URL served by MapFileManagerFileServer().
        /// Expiry is governed by FileManager:DefaultSecureUrlExpiryHours (see §11).
        /// </summary>
        Task<SecureUrlResult> GetSecureUrlAsync(
            string filePath, TimeSpan? expiry = null,
            CancellationToken cancellationToken = default);

        // ── Delete ───────────────────────────────────────────────────────────
        Task DeleteAsync(
            string filePath, CancellationToken cancellationToken = default);

        // ── Query ────────────────────────────────────────────────────────────
        Task<bool> ExistsAsync(
            string filePath, CancellationToken cancellationToken = default);

        Task<FileMetadata> GetMetadataAsync(
            string filePath, CancellationToken cancellationToken = default);

        Task<FileListResult> ListAsync(
            string folderPath, CancellationToken cancellationToken = default);

        // ── Manage (v1.1) ────────────────────────────────────────────────────
        Task MoveAsync(string sourcePath, string destinationPath,
            CancellationToken cancellationToken = default);

        Task CopyAsync(string sourcePath, string destinationPath,
            CancellationToken cancellationToken = default);
    }
}
```

> **Design decision (no capabilities flag).** Per requirement, the interface is uniform — there is no `Capabilities` property and callers never branch on provider type. `GetSecureUrlAsync` is honored by **both** providers. For the File System provider this is made true by shipping the serving endpoint (`MapFileManagerFileServer()`, §11.4) in the package, so consumers do not hand-write a controller.

---

## 7. Models

### FileUploadRequest
```csharp
public sealed class FileUploadRequest
{
    /// <summary>Relative path including filename, e.g. "invoices/2026/06/inv001.pdf".</summary>
    public required string FilePath { get; init; }

    /// <summary>MIME type. If null, auto-detected from extension.</summary>
    public string? ContentType { get; init; }

    /// <summary>Optional metadata stored alongside the file.</summary>
    public Dictionary<string, string>? Metadata { get; init; }

    /// <summary>If true and the file exists, overwrite. Default false (throws).</summary>
    public bool Overwrite { get; init; } = false;
}
```

### FileUploadResult
```csharp
public sealed class FileUploadResult
{
    public required string StoredPath { get; init; }
    public required long FileSizeBytes { get; init; }
    public required string ContentType { get; init; }
    public required DateTimeOffset UploadedAt { get; init; }
    public string? SecureUrl { get; init; }   // populated if GenerateUrlOnUpload = true
    public string Provider { get; init; } = string.Empty;
}
```

### FileDownloadResult  *(new — lose-nothing, disposable)*
```csharp
/// <summary>
/// Result of a download. Owns the content stream — dispose with 'await using'.
/// Carries metadata received in the same provider call, so callers do not need
/// a separate GetMetadataAsync round-trip.
/// </summary>
public sealed class FileDownloadResult : IAsyncDisposable, IDisposable
{
    public required Stream Content { get; init; }
    public required string ContentType { get; init; }
    public required long FileSizeBytes { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();

    public ValueTask DisposeAsync() => Content.DisposeAsync();
    public void Dispose() => Content.Dispose();
}
```

### FileMetadata
```csharp
public sealed class FileMetadata
{
    public required string FilePath { get; init; }
    public required long FileSizeBytes { get; init; }
    public required string ContentType { get; init; }
    public required DateTimeOffset LastModified { get; init; }
    public DateTimeOffset? CreatedOn { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
}
```

### FileListResult
```csharp
public sealed class FileListResult
{
    public required string FolderPath { get; init; }
    public required IReadOnlyList<FileMetadata> Files { get; init; }
}
```

### SecureUrlResult
```csharp
public sealed class SecureUrlResult
{
    public required string Url { get; init; }
    /// <summary>Null when the URL never expires (see §11 expiry rule).</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
    public required string FilePath { get; init; }
}
```

> Down-level TFMs (net462, netstandard2.0, net6.0) do not ship the runtime types that back `required`/`init` (`IsExternalInit`, `RequiredMemberAttribute`, `CompilerFeatureRequiredAttribute`). PolySharp supplies them at build time — see §18.

---

## 8. Configuration Schema

### appsettings.json (Production default — Azure Blob, Managed Identity)
```jsonc
{
  "FileManager": {
    "Provider": "AzureBlob",
    "RootContainer": "rsk-app-files",
    "MaxFileSizeBytes": 104857600,
    "DefaultSecureUrlExpiryHours": 1,   // >0 = hours, 0 = never, <0 = invalid (see §11)
    "GenerateUrlOnUpload": true,
    "AllowedExtensions": [ ".pdf", ".png", ".jpg", ".docx", ".xlsx" ],
    "EnableContentSniffing": true,
    "AzureBlob": {
      "UseManagedIdentity": true,
      "AccountName": "rskstorageaccount",
      "ConnectionString": "",
      "RetryMaxAttempts": 3,
      "RetryBaseDelaySeconds": 2
    },
    "FileSystem": {
      "RootPath": "C:\\RSK\\Uploads",
      "ServeBaseUrl": "https://myapp.rsk.com/files",
      "TokenSecret": "REPLACE_WITH_STRONG_SECRET_MIN_32_CHARS",
      "RemoveEmptyFolders": false
    }
  }
}
```

### appsettings.Development.json (FileSystem override)
```jsonc
{
  "FileManager": {
    "Provider": "FileSystem",
    "FileSystem": {
      "RootPath": "C:\\Dev\\RSKUploads",
      "ServeBaseUrl": "https://localhost:5001/files",
      "TokenSecret": "dev-secret-not-for-production-min-32-characters"
    }
  }
}
```

### web.config (.NET Framework 4.6.x)
```xml
<appSettings>
  <add key="FileManager:Provider" value="FileSystem" />
  <add key="FileManager:RootContainer" value="rsk-app-files" />
  <add key="FileManager:MaxFileSizeBytes" value="104857600" />
  <add key="FileManager:DefaultSecureUrlExpiryHours" value="1" />
  <add key="FileManager:FileSystem:RootPath" value="C:\RSK\Uploads" />
  <add key="FileManager:FileSystem:ServeBaseUrl" value="https://myapp.rsk.com/files" />
  <add key="FileManager:FileSystem:TokenSecret" value="REPLACE_WITH_STRONG_SECRET_MIN_32_CHARS" />
</appSettings>
```

### Options model & startup validation
```csharp
public enum StorageProvider { AzureBlob, FileSystem }

public sealed class FileManagerOptions
{
    public StorageProvider Provider { get; init; }
    public string RootContainer { get; init; } = "";
    public long MaxFileSizeBytes { get; init; } = 104_857_600;     // 100 MB
    public int DefaultSecureUrlExpiryHours { get; init; } = 1;     // >0 hours, 0 never, <0 invalid
    public bool GenerateUrlOnUpload { get; init; } = true;
    public string[] AllowedExtensions { get; init; } = Array.Empty<string>();
    public bool EnableContentSniffing { get; init; } = true;
    public AzureBlobOptions AzureBlob { get; init; } = new();
    public FileSystemOptions FileSystem { get; init; } = new();

    public void Validate()
    {
        if (DefaultSecureUrlExpiryHours < 0)
            throw new FileManagerConfigException(
                "FileManager:DefaultSecureUrlExpiryHours cannot be negative.");

        if (MaxFileSizeBytes <= 0)
            throw new FileManagerConfigException(
                "FileManager:MaxFileSizeBytes must be greater than zero.");

        switch (Provider)
        {
            case StorageProvider.AzureBlob:
                AzureBlob.Validate(DefaultSecureUrlExpiryHours, RootContainer);
                break;
            case StorageProvider.FileSystem:
                FileSystem.Validate();
                break;
            default:
                throw new FileManagerConfigException($"Unknown provider: {Provider}");
        }
    }
}
```

```csharp
public sealed class AzureBlobOptions
{
    public bool UseManagedIdentity { get; init; }
    public string AccountName { get; init; } = "";
    public string ConnectionString { get; init; } = "";
    public int RetryMaxAttempts { get; init; } = 3;
    public int RetryBaseDelaySeconds { get; init; } = 2;

    public void Validate(int expiryHours, string rootContainer)
    {
        if (string.IsNullOrWhiteSpace(rootContainer))
            throw new FileManagerConfigException("FileManager:RootContainer is required for AzureBlob.");

        if (UseManagedIdentity)
        {
            if (string.IsNullOrWhiteSpace(AccountName))
                throw new FileManagerConfigException(
                    "FileManager:AzureBlob:AccountName is required when UseManagedIdentity = true.");

            // User Delegation SAS is capped at 7 days; 'never expire' is impossible.
            if (expiryHours == 0)
                throw new FileManagerConfigException(
                    "Never-expiring URLs (DefaultSecureUrlExpiryHours = 0) are not supported with " +
                    "Managed Identity. Azure caps a User Delegation SAS at 7 days (168 hours). " +
                    "Use 1–168, or switch to a connection string.");
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
```

```csharp
public sealed class FileSystemOptions
{
    public string RootPath { get; init; } = "";
    public string ServeBaseUrl { get; init; } = "";
    public string TokenSecret { get; init; } = "";

    /// <summary>
    /// If true, deleting a file also removes parent folders that become empty,
    /// walking up to RootPath. Default false. WARNING: not safe under concurrent
    /// writers (TOCTOU) — can race with another process creating files in the folder.
    /// </summary>
    public bool RemoveEmptyFolders { get; init; } = false;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RootPath))
            throw new FileManagerConfigException("FileManager:FileSystem:RootPath is required.");
        if (string.IsNullOrWhiteSpace(ServeBaseUrl))
            throw new FileManagerConfigException("FileManager:FileSystem:ServeBaseUrl is required.");
        if (string.IsNullOrWhiteSpace(TokenSecret) ||
            TokenSecret.Length < 32 ||
            TokenSecret.StartsWith("REPLACE_WITH", StringComparison.OrdinalIgnoreCase))
            throw new FileManagerConfigException(
                "FileManager:FileSystem:TokenSecret must be set to a strong secret of at least 32 characters.");
    }
}
```

---

## 9. DI Registration (.NET Core / .NET 5+)

```csharp
// Program.cs — one line. Provider chosen by config; no code change to switch.
builder.Services.AddFileManager(builder.Configuration);

// For the FileSystem secure-URL endpoint (harmless when Provider = AzureBlob):
app.MapFileManagerFileServer();
```

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFileManager(
        this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("FileManager");
        var options = section.Get<FileManagerOptions>()
            ?? throw new FileManagerConfigException("FileManager configuration section is missing.");

        options.Validate();                       // fail fast at startup
        services.Configure<FileManagerOptions>(section);

        // Provider is registered as a SINGLETON; the Azure client (and its socket
        // pool) is therefore created once and reused (see §13.1).
        services.AddSingleton<IFileManagerService>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<FileManagerOptions>>().Value;
            var lf   = sp.GetRequiredService<ILoggerFactory>();
            return opts.Provider switch
            {
                StorageProvider.AzureBlob  => new AzureBlobProvider(opts, lf.CreateLogger<AzureBlobProvider>()),
                StorageProvider.FileSystem => new FileSystemProvider(opts, lf.CreateLogger<FileSystemProvider>()),
                _ => throw new FileManagerConfigException($"Unknown provider: {opts.Provider}")
            };
        });
        return services;
    }
}
```

> **Logger note.** The provider receives `ILogger<AzureBlobProvider>` / `ILogger<FileSystemProvider>` so that log categories are provider-specific and filterable in Seq/App Insights.

---

## 10. .NET Framework 4.6.x Registration (No DI)

```csharp
// Global.asax.cs (Application_Start)
FileManagerFactory.Initialize(ConfigurationManager.AppSettings);   // validates config, fail fast

// Anywhere in the app
var service = FileManagerFactory.GetService();                     // cached singleton
await service.UploadAsync(request, stream);
```

`FileManagerFactory` reads the flat `FileManager:*` keys, builds a `FileManagerOptions`, calls `Validate()`, constructs the configured provider once, and caches it. The same provider instance is returned for the process lifetime (mirrors the singleton DI registration).

---

## 11. Secure URL Architecture *(reworked)*

Secure-URL generation is **not** uniform across Azure auth modes. There are three distinct code paths, plus a global expiry rule.

### 11.1 Expiry rule (applies to all providers)

`DefaultSecureUrlExpiryHours` governs the default. A per-call `expiry` argument overrides it.

| Config value | Meaning | Notes |
|---|---|---|
| `> 0` | URL expires in N hours | Valid for all providers (Azure MSI capped at 168). |
| `0` | URL never expires | FileSystem and **connection-string** Azure only. **Rejected at startup for Managed Identity.** |
| `< 0` | Invalid | `FileManagerConfigException` thrown at startup. |

`SecureUrlResult.ExpiresAt` is `null` when the URL never expires.

### 11.2 Azure with connection string → **Service SAS**

The connection string carries the account key, so the SAS is signed locally (no network call). "Never expire" is approximated with a far-future expiry.

```csharp
private string GenerateServiceSas(string blobName, DateTimeOffset? expiresOn)
{
    var blob = _container.GetBlobClient(blobName);   // built from the connection string
    var sas = new BlobSasBuilder
    {
        BlobContainerName = _container.Name,
        BlobName          = blobName,
        Resource          = "b",
        ExpiresOn         = expiresOn ?? DateTimeOffset.MaxValue   // null = never
    };
    sas.SetPermissions(BlobSasPermissions.Read);     // READ only — never write/delete on a SAS
    return blob.GenerateSasUri(sas).ToString();      // requires StorageSharedKeyCredential
}
```

### 11.3 Azure with Managed Identity → **User Delegation SAS**

There is no account key. The SAS is signed with a short-lived **user delegation key** obtained from Azure (a network call), and is **hard-capped at 7 days**. The delegation key is cached and reused across many URL generations until near expiry.

```csharp
private async Task<string> GenerateUserDelegationSasAsync(
    string blobName, DateTimeOffset expiresOn, CancellationToken ct)
{
    var key = await GetCachedUserDelegationKeyAsync(expiresOn, ct).ConfigureAwait(false);

    var sas = new BlobSasBuilder
    {
        BlobContainerName = _container.Name,
        BlobName          = blobName,
        Resource          = "b",
        ExpiresOn         = expiresOn
    };
    sas.SetPermissions(BlobSasPermissions.Read);

    var token = sas.ToSasQueryParameters(key, _accountName).ToString();
    return $"{_container.Uri}/{Uri.EscapeDataString(blobName)}?{token}";
}
```

**RBAC requirement.** The managed identity needs **Storage Blob Delegator** (to obtain the delegation key) plus a data role such as **Storage Blob Data Reader**. Document this in the deployment runbook; a missing Delegator role yields a 403 at `GetUserDelegationKeyAsync`.

### 11.4 FileSystem → **HMAC-signed URL** + serving endpoint

The File System has no SAS. The provider signs an HMAC-SHA256 token; the package ships the endpoint that validates it and streams the file, so consumers write no controller.

URL shape:
```
https://myapp.rsk.com/files/invoices/inv001.pdf?expires=1749999999&token=<url-safe-base64>
```

**Token rules (all mandatory):**
1. The signed payload covers **path AND expiry**, joined by an unambiguous separator, so a token cannot be swapped between files.
2. Comparison uses `CryptographicOperations.FixedTimeEquals` (constant-time; no timing attack).
3. The token is **URL-safe Base64** (`+`→`-`, `/`→`_`, padding trimmed).
4. `never expire` (expiry rule = 0) signs path only and omits the expiry component.
5. The serving endpoint re-runs `PathSanitizer` before touching disk (defense in depth).

```csharp
public sealed class FileSystemTokenService
{
    private readonly byte[] _key;
    public FileSystemTokenService(string secret) => _key = Encoding.UTF8.GetBytes(secret);

    public string Create(string path, DateTimeOffset? expiresOn)
    {
        var normalized = PathSanitizer.Sanitize(path);
        var payload = expiresOn is null
            ? $"{normalized}"
            : $"{normalized}\n{expiresOn.Value.ToUnixTimeSeconds()}";
        using var hmac = new HMACSHA256(_key);
        return ToUrlSafe(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    public bool Validate(string path, long? expiresUnix, string providedToken)
    {
        if (expiresUnix is not null &&
            DateTimeOffset.FromUnixTimeSeconds(expiresUnix.Value) < DateTimeOffset.UtcNow)
            return false;

        var expected = Create(path, expiresUnix is null
            ? (DateTimeOffset?)null
            : DateTimeOffset.FromUnixTimeSeconds(expiresUnix.Value));

        return CryptographicOperations.FixedTimeEquals(
            FromUrlSafe(providedToken), FromUrlSafe(expected));
    }

    private static string ToUrlSafe(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] FromUrlSafe(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
    }
}
```

```csharp
// RSK.FileManager.AspNetCore — one line in the app; no hand-written controller
app.MapFileManagerFileServer();   // GET {ServeBaseUrl}/{*path}?expires=&token=
```

---

## 12. Security Architecture

### 12.1 Path Sanitization (`PathSanitizer`)
Applied to every path before any I/O and again inside the serving endpoint.

- Strip leading `/`, `\`, `.`
- Reject `..` traversal sequences
- Reject null bytes and control characters
- Normalize to forward slashes
- Enforce max path length (**250** chars)
- Trim whitespace
- On violation → `FileManagerValidationException` (logged at Warning with original input)

### 12.2 File Validation (`FileValidator`) — order matters
1. Null/empty path → `FileManagerValidationException`
2. Path sanitization → throw on traversal
3. Size > `MaxFileSizeBytes` → `FileManagerValidationException`
4. Extension not in `AllowedExtensions` (when the list is non-empty) → `FileManagerValidationException`
5. **Content sniffing (v1.0, when `EnableContentSniffing = true`)** — read the leading bytes (magic numbers); reject content that doesn't match its claimed extension, and hard-block known-dangerous signatures (e.g. `MZ` executables) regardless of name. The stream is rewound (`Seek(0)`) so the upload still receives all bytes; non-seekable streams are buffered for the header read.

### 12.3 Delete authorization
The package does **not** gate `DeleteAsync` — it trusts the caller. Consuming apps must enforce authorization before calling it. Documented prominently in README and MIGRATION.

---

## 13. Provider Implementation Details

### 13.1 Azure Blob Provider
- **Single client reuse.** One `BlobContainerClient` is created in the constructor and reused for every operation. The provider is a singleton, so its internal socket pool is shared — clients are never constructed per call (avoids socket/port exhaustion).
- **Flat namespace.** "Folders" are path prefixes; `A/B/C/file.pdf` is a blob name. No folder creation needed; an empty prefix ceases to exist automatically.
- **Retry (Polly 8).** Exponential backoff, `RetryMaxAttempts` (default 3), base delay `RetryBaseDelaySeconds` (2s → 4s → 8s), retrying transient `RequestFailedException` (429, 500, 503). Wrapped failures surface as `FileManagerTransientException`.
- **Auth.** Connection string → `BlobServiceClient(connStr)`. Managed Identity → `BlobServiceClient(uri, new DefaultAzureCredential())`.

### 13.2 File System Provider
- **Auto folder creation.** `Directory.CreateDirectory()` builds the full path recursively.
- **Empty-folder cleanup is opt-in** (`RemoveEmptyFolders`, default false). When enabled, after a delete the provider walks up to `RootPath` removing now-empty directories. Documented TOCTOU caveat: unsafe under concurrent writers.
- **Concurrency.** Reads open with `FileShare.Read`; writes take an exclusive lock. For multi-instance deployments a shared network path (UNC/NFS) or a distributed lock is required — documented limitation.

---

## 14. Async & Threading Model *(new)*

This library targets classic ASP.NET (`net462`), whose `SynchronizationContext` deadlocks when a caller blocks on an async method (`.Result`/`.Wait()`) that resumes on the captured context.

**Rules enforced in the library:**
1. **`ConfigureAwait(false)` on every internal await**, on every I/O path, in every package. A library never needs the caller's context.
2. **No `async void`** anywhere except (hypothetical) UI handlers — it cannot be awaited and its exceptions crash the process.
3. **Async all the way** — only `Task`/`Task<T>`-returning methods; no sync-over-async wrappers, no `.Result`/`.Wait()` inside the library.
4. **Enforced in CI** by a Roslyn analyzer (e.g. `ConfigureAwaitChecker.Analyzer` or an equivalent rule) so a missed `ConfigureAwait(false)` fails the build.

Consumer guidance (README): go async all the way (`Task`-returning controller actions/handlers); never `async void`. Because the library is `ConfigureAwait(false)` internally, it is safe even from legacy callers that block.

---

## 15. Logging Strategy

`ILogger<TProvider>` on every operation.

| Event | Level | Details |
|---|---|---|
| Upload started | Debug | path, size, provider |
| Upload completed | Information | path, size, durationMs |
| Upload failed | Error | path, exception |
| Download started/completed | Debug | path, durationMs |
| File deleted | Information | path |
| Folder removed (opt-in) | Information | folderPath |
| Secure URL generated | Debug | path, expiry, sasType (Service/UserDelegation/HMAC) |
| Delegation key refreshed | Debug | expiresOn |
| Path sanitization violation | Warning | originalPath |
| File validation failure | Warning | path, reason |
| Content sniffing rejection | Warning | path, detectedSignature |
| Retry attempt | Warning | attempt, delay, exception |
| Config validation failure | Critical | field, reason |

Structured template example:
```
[RSK.FileManager] Upload completed {FilePath} {FileSizeBytes}b via {Provider} in {DurationMs}ms
```

---

## 16. Error Handling

```
FileManagerException                    ← base; always catchable
├── FileManagerNotFoundException        ← file/folder missing
├── FileManagerValidationException      ← bad input (path, size, extension, content)
├── FileManagerProviderException        ← storage backend failure
│   └── FileManagerTransientException   ← temporary; retry-safe (after Polly exhausts)
└── FileManagerConfigException          ← startup misconfiguration (fail fast)
```

```csharp
try
{
    await using var file = await _files.DownloadAsync(path, ct);
    return File(file.Content, file.ContentType);
}
catch (FileManagerNotFoundException)            { return NotFound(); }
catch (FileManagerValidationException ex)       { return BadRequest(ex.Message); }
catch (FileManagerProviderException ex)         { _logger.LogError(ex, "storage failure"); return StatusCode(503); }
catch (FileManagerException ex)                 { return StatusCode(500, ex.Message); }
```

---

## 17. Base Provider (shared logic)

```csharp
public abstract class FileManagerProviderBase : IFileManagerService
{
    protected readonly FileManagerOptions Options;
    protected readonly ILogger Logger;

    protected FileManagerProviderBase(FileManagerOptions options, ILogger logger)
        => (Options, Logger) = (options, logger);

    // Shared pre-upload pipeline
    protected async Task ValidateUploadAsync(string path, long size, Stream content, CancellationToken ct)
    {
        PathSanitizer.Validate(path);
        FileValidator.ValidateSize(size, Options);
        FileValidator.ValidateExtension(path, Options);
        if (Options.EnableContentSniffing)
            await FileValidator.ValidateContentAsync(content, path, ct).ConfigureAwait(false);
    }

    protected string NormalizePath(string path) => PathSanitizer.Sanitize(path);

    // Provider-specific
    protected abstract Task<FileUploadResult> UploadCoreAsync(/* ... */);
    protected abstract Task<FileDownloadResult> DownloadCoreAsync(/* ... */);
    protected abstract Task DeleteCoreAsync(/* ... */);
    // etc.
}
```

---

## 18. Multi-Target Build

`Directory.Build.props` (shared):
```xml
<Project>
  <PropertyGroup>
    <TargetFrameworks>net462;netstandard2.0;net6.0;net8.0</TargetFrameworks>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

  <!-- Polyfills 'required'/'init'/etc. on down-level TFMs. Build-only; not exposed to consumers. -->
  <ItemGroup>
    <PackageReference Include="PolySharp" Version="1.*" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

Azure provider csproj:
```xml
<ItemGroup>
  <PackageReference Include="Azure.Storage.Blobs" Version="12.*" />
  <PackageReference Include="Polly" Version="8.*" />
</ItemGroup>
<ItemGroup Condition="'$(TargetFramework)' == 'net462'">
  <!-- net462 may need an older pinned Azure SDK + binding redirects in the consuming app -->
  <PackageReference Include="Azure.Storage.Blobs" Version="12.13.*" />
</ItemGroup>
```

> **net462 binding redirects.** On .NET Framework only one version of each assembly loads per app domain. The Azure SDK pulls `System.Memory`, `System.Buffers`, `System.Text.Json`, `Microsoft.Bcl.AsyncInterfaces`, etc.; the consuming app must carry correct `<bindingRedirect>` entries (enable `<AutoGenerateBindingRedirects>true</AutoGenerateBindingRedirects>`). FileSystem-only net462 apps avoid this entirely by not referencing the Azure package (§4). Captured in the Risk Register (§24).

---

## 19. NuGet Package Metadata (per package)

```xml
<PropertyGroup>
  <Authors>RSK Development Team</Authors>
  <Company>RSK</Company>
  <Product>RSK FileManager</Product>
  <Version>1.0.0</Version>
  <PackageTags>rsk;filestorage;azure;blob;filesystem;upload</PackageTags>
  <PackageRequireLicenseAcceptance>false</PackageRequireLicenseAcceptance>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  <IncludeSymbols>true</IncludeSymbols>
  <SymbolPackageFormat>snupkg</SymbolPackageFormat>
  <PackageReadmeFile>README.md</PackageReadmeFile>
</PropertyGroup>
```
All packages in the family share one version number per release for simplicity.

---

## 20. Versioning Strategy (SemVer)

| Change | Bump | Example |
|---|---|---|
| Bug fix, no API change | Patch | 1.0.0 → 1.0.1 |
| New non-breaking method | Minor | 1.0.1 → 1.1.0 |
| Interface/config breaking change | Major | 1.1.0 → 2.0.0 |
| Security patch (critical) | Patch + advisory | 1.0.0 → 1.0.1 |

**Rule.** Never change `IFileManagerService` or `FileManagerOptions` shape in a minor/patch. Any breaking change to `RSK.FileManager.Abstractions` = major. Because the contract is its own package, an Azure SDK major upgrade only bumps `RSK.FileManager.AzureBlob`, not the abstraction.

---

## 21. Testing Strategy

**Unit (RSK.FileManager.UnitTests)** — `PathSanitizer` attack vectors; `FileValidator` size/extension/magic-byte boundaries; `FileManagerOptions.Validate()` incl. the MSI-never-expire rule; `FileSystemTokenService` create/validate, path-swap rejection, constant-time comparison, URL-safe round-trip; SAS path selection by auth mode (mocked credential).

**Integration (RSK.FileManager.IntegrationTests)** — Azurite (Docker) for Blob; temp dir for FS. Full lifecycle upload → list → download → delete; auto folder creation; opt-in empty-folder removal; Service SAS generation + expiry; HMAC URL generation + endpoint validation; Polly retry under simulated transient failures. (Use Azurite/real backends; avoid mocking `BlobContainerClient`.)

**Compatibility (RSK.FileManager.CompatTests)** — target `net462`; verify all APIs compile and run; assert no deadlock when a sync caller blocks on the library (the `ConfigureAwait(false)` guarantee).

**Coverage target: ≥ 85%.**

---

## 22. CI/CD

**ci.yml (every PR):** restore → build all TFMs (warnings-as-errors) → unit tests → integration tests (Azurite in Docker) → coverage gate ≥ 85% → security scan (`dotnet list package --vulnerable` + analyzer) → `ConfigureAwait` analyzer must pass → pack (dry run).

**publish.yml (tag `v*`):** all CI steps → pack all packages → push to RSK Azure Artifacts → GitHub release with changelog.

---

## 23. Architect Recommendations

1. **Managed Identity over connection strings in prod** — but remember it forces the User Delegation SAS path and the 7-day expiry cap (§11.3). Both paths are implemented and tested.
2. **Never expose physical paths** — `ServeBaseUrl` must never leak `RootPath`; the serving endpoint re-sanitizes and serves only within `RootPath`.
3. **Content sniffing is v1.0, not optional theatre** — extension checks alone are bypassed by renamed executables (§12.2).
4. **Azure soft delete** — enable on the storage account (30 days). `DeleteAsync` hard-deletes; a `SoftDelete` option is a v1.1 candidate.
5. **Blob versioning** — consider `GetVersionsAsync` in v2 for document-management scenarios.
6. **FileSystem is not multi-instance safe** — only works across instances on a shared UNC/NFS path; otherwise use Blob (or Azurite locally). Documented limitation.
7. **Async-only, deadlock-safe by construction** — §14. No `async void`.
8. **Telemetry hook** — `IFileManagerTelemetry` in v1.1 so apps can plug App Insights without coupling the package.
9. **Health check** — `AddFileManagerHealthCheck()` (ships in AspNetCore) pings blob container access / FS root write permission.
10. **Rate-limit awareness** — Polly handles transient 429s; for batch work add `BulkUploadAsync` with `SemaphoreSlim` concurrency (v1.1).

---

## 24. Risk Register

| Risk | Impact | Mitigation |
|---|---|---|
| Azure SDK breaking changes | High | Pin major; contract isolated in Abstractions package |
| net462 async deadlocks | High | `ConfigureAwait(false)` everywhere + analyzer + compat test |
| net462 Azure SDK binding redirects | Medium | AutoGenerateBindingRedirects; FS-only apps skip Azure package |
| MSI + "never expire" requested | Medium | Rejected at startup with actionable message (§8/§11) |
| User delegation key (7-day cap) misunderstood | Medium | Documented; validated; key cached |
| FileSystem on multi-instance deployment | High | Documented; recommend Blob/Azurite |
| HMAC secret exposure / default value | High | Min 32 chars + reject placeholder at startup |
| Empty-folder cleanup race (TOCTOU) | Medium | Opt-in, off by default, documented |
| Large `byte[]` overload memory pressure | Medium | Warn-log when byte[] > 50 MB; recommend stream overload |
| `required`/`init` fail to compile down-level | High | PolySharp polyfills (§18) |

---

## 25. Delivery Phases

### Phase 1 — Core (v1.0.0) — ~3 weeks
- [ ] Solution scaffold, package split, Directory.Build.props, PolySharp, analyzers
- [ ] Abstractions: interface, models (incl. `FileDownloadResult`), options, exceptions
- [ ] `PathSanitizer`, `FileValidator` (size + extension + **magic-byte sniffing**)
- [ ] AzureBlob provider: upload, download, delete, exists, list, metadata; **Service SAS + User Delegation SAS**; Polly retry; singleton client
- [ ] FileSystem provider: same ops; HMAC token service (path+expiry, FixedTimeEquals, URL-safe); opt-in folder cleanup
- [ ] `AddFileManager` (Core) + `FileManagerFactory` (net462)
- [ ] `MapFileManagerFileServer` endpoint (AspNetCore)
- [ ] Startup config validation (incl. MSI expiry rule)
- [ ] `ConfigureAwait(false)` throughout + analyzer in CI
- [ ] Unit + integration (Azurite) + net462 compat tests; ≥ 85% coverage
- [ ] README, CONFIGURATION.md, MIGRATION.md; CI pipeline; publish to internal feed

### Phase 2 — Enhanced (v1.1.0) — ~2 weeks
- [ ] Move/Copy; Azure soft delete; `IFileManagerTelemetry`; `AddFileManagerHealthCheck`; `BulkUploadAsync`

### Phase 3 — Advanced (v2.0.0) — by demand
- [ ] Blob versioning; AWS S3 provider; chunked/resumable upload; compression; audit-log provider

---

## 26. Change Log vs v1.0

1. Renamed product/namespace/interface/config/exceptions to **RSK.FileManager** / `IFileManagerService` / `FileManager:` / `FileManager*Exception`.
2. **Package split** into Abstractions + AzureBlob + FileSystem + glue + AspNetCore (§4).
3. **Secure URLs corrected**: Service SAS (key) vs User Delegation SAS (MSI, 7-day cap); explicit expiry rule (`>0`/`0`/`<0`) with MSI-never-expire rejected at startup (§11).
4. **HMAC hardened**: path+expiry signed, `FixedTimeEquals`, URL-safe Base64; serving endpoint shipped (§11.4).
5. **`ConfigureAwait(false)`** mandated + analyzer; removed the `async void` recommendation (§14).
6. **`DownloadAsync`** now returns disposable, lose-nothing `FileDownloadResult` (§6/§7).
7. **Magic-byte content sniffing** moved into v1.0 (§12.2).
8. **Empty-folder cleanup** is opt-in, off by default (§13.2).
9. **PolySharp** polyfills for `required`/`init` on down-level TFMs (§18).
10. **Polly** (lean) instead of Microsoft.Extensions.Resilience.
11. **Singleton storage client** reuse to avoid socket exhaustion (§13.1).
12. Path cap set to **250**; logger categories made provider-specific; binding-redirect risk documented.

---

*Prepared by RSK Solution Architecture. Approved for Phase 1.*

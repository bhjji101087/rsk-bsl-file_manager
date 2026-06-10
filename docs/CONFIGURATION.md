# RSK.FileManager — Configuration

All settings live under the `FileManager` section. Provider selection is config-driven:
the same compiled app runs against the File System in development and Azure Blob in
UAT/Production by changing configuration only.

## Root keys

| Key | Type | Default | Notes |
|---|---|---|---|
| `Provider` | `AzureBlob` \| `FileSystem` | — | Which backend to use. |
| `RootContainer` | string | — | Azure container name (required for AzureBlob). |
| `MaxFileSizeBytes` | long | `104857600` (100 MB) | Upload size limit. |
| `DefaultSecureUrlExpiryHours` | int | `1` | Secure-URL lifetime. See the expiry rule below. |
| `GenerateUrlOnUpload` | bool | `true` | Populate `SecureUrl` on the upload result. |
| `AllowedExtensions` | string[] | `[]` | Whitelist; empty = allow any. |
| `EnableContentSniffing` | bool | `true` | Validate content by magic bytes (blocks renamed executables). |

### Secure-URL expiry rule (`DefaultSecureUrlExpiryHours`)

| Value | Meaning | Notes |
|---|---|---|
| `> 0` | Expires in N hours | Valid for all providers (Azure Managed Identity is capped at 168). |
| `0` | Never expires | FileSystem and **connection-string** Azure only. |
| `< 0` | Invalid | Throws `FileManagerConfigException` at startup. |

> **Managed Identity caveat:** `0` (never expire) is **rejected at startup** when the Azure
> provider uses Managed Identity, because a User Delegation SAS is capped at 7 days by Azure.
> Use `1`–`168`, or switch to a connection string.

## Azure Blob (`FileManager:AzureBlob`)

| Key | Type | Default | Notes |
|---|---|---|---|
| `UseManagedIdentity` | bool | `false` | `true` → User Delegation SAS (recommended in prod). |
| `AccountName` | string | — | Required when `UseManagedIdentity = true`. |
| `ConnectionString` | string | — | Required when `UseManagedIdentity = false`. |
| `RetryMaxAttempts` | int | `3` | Polly retry attempts for transient 429/500/503. |
| `RetryBaseDelaySeconds` | int | `2` | Exponential backoff base (2s, 4s, 8s...). |

**Managed Identity RBAC:** the identity needs **Storage Blob Data Reader** (or Contributor for
writes) plus **Storage Blob Delegator** (to obtain the user delegation key for secure URLs).

## File System (`FileManager:FileSystem`)

| Key | Type | Default | Notes |
|---|---|---|---|
| `RootPath` | string | — | Root directory for stored files. |
| `ServeBaseUrl` | string | — | Public base URL the serving endpoint is mounted on. Must not expose `RootPath`. |
| `TokenSecret` | string | — | HMAC signing secret. **Minimum 32 characters**; the placeholder value is rejected. |
| `RemoveEmptyFolders` | bool | `false` | Remove empty parent folders after delete. **Not safe under concurrent writers (TOCTOU).** |

> File System secure URLs require `app.MapFileManagerFileServer()` (from
> `RSK.FileManager.AspNetCore`). It is a no-op when the provider is AzureBlob, so the same
> registration is safe in every environment.

## Examples

### appsettings.json (Production — Azure Blob, Managed Identity)
```jsonc
{
  "FileManager": {
    "Provider": "AzureBlob",
    "RootContainer": "rsk-app-files",
    "DefaultSecureUrlExpiryHours": 1,
    "AzureBlob": { "UseManagedIdentity": true, "AccountName": "rskstorageaccount" }
  }
}
```

### appsettings.Development.json (File System override)
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
  <add key="FileManager:FileSystem:RootPath" value="C:\RSK\Uploads" />
  <add key="FileManager:FileSystem:ServeBaseUrl" value="https://myapp.rsk.com/files" />
  <add key="FileManager:FileSystem:TokenSecret" value="REPLACE_WITH_STRONG_SECRET_MIN_32_CHARS" />
</appSettings>
```

## Registration

```csharp
// .NET Core / .NET 5+
builder.Services.AddFileManager(builder.Configuration);
app.MapFileManagerFileServer();                 // FileSystem secure URLs (no-op for Azure)
builder.Services.AddHealthChecks().AddFileManagerHealthCheck();
```

```csharp
// .NET Framework 4.6.x (no DI)
FileManagerFactory.Initialize(ConfigurationManager.AppSettings);
var files = FileManagerFactory.GetService();
```

# RSK.FileManager — Migration Guide

How to replace an existing per-project file-handling service (e.g. `IFileService`,
`IUploadService`, or direct Azure SDK / `System.IO` calls) with RSK.FileManager.

## Steps

1. **Install the packages.**
   - Apps reference `RSK.FileManager` (registration glue, pulls both providers).
   - ASP.NET Core apps also reference `RSK.FileManager.AspNetCore` (endpoint + health check).

2. **Add configuration.** Add a `FileManager` section (see `CONFIGURATION.md`). Choose
   `FileSystem` for dev and `AzureBlob` for UAT/Prod via environment-specific config.

3. **Register.**
   - .NET Core: `builder.Services.AddFileManager(builder.Configuration);` and
     `app.MapFileManagerFileServer();`
   - .NET Framework: `FileManagerFactory.Initialize(ConfigurationManager.AppSettings);`

4. **Replace the dependency.** Swap your old interface for `IFileManagerService`.

5. **Map the calls:**

   | Old (typical) | New |
   |---|---|
   | `Save(path, bytes)` / `Upload(stream)` | `UploadAsync(new FileUploadRequest { FilePath = path }, stream/bytes)` |
   | `Read(path)` / `GetStream(path)` | `DownloadAsync(path)` → `FileDownloadResult` (dispose it) |
   | `GetUrl(path)` / SAS helper | `GetSecureUrlAsync(path, expiry)` |
   | `Delete(path)` | `DeleteAsync(path)` |
   | `Exists(path)` | `ExistsAsync(path)` |
   | `GetInfo(path)` | `GetMetadataAsync(path)` |
   | `ListFolder(path)` | `ListAsync(path)` |

6. **Remove old code.** Delete the bespoke Azure SDK / `System.IO` plumbing.

7. **Test.** Run on dev (FileSystem) and staging (Azure Blob) — no code changes between them.

## Important behavioural notes

- **Authorization for delete is the caller's responsibility.** `DeleteAsync` is **not** gated by
  the package. Enforce your own authorization before calling it.

- **`DownloadAsync` returns an owned stream.** Dispose the `FileDownloadResult`
  (`await using` on .NET 6+, `using` on .NET Framework). It also carries content type, size and
  metadata, so you do not need a separate `GetMetadataAsync` call.

- **Managed Identity + never-expire URLs is unsupported.** With `UseManagedIdentity = true`,
  `DefaultSecureUrlExpiryHours = 0` is rejected at startup (User Delegation SAS is capped at
  7 days). Use `1`–`168`, or a connection string.

- **File System secure URLs need the endpoint.** Call `app.MapFileManagerFileServer()` once
  (it is a no-op for Azure). Without it, FileSystem secure URLs return 404.

- **File System is not multi-instance safe** unless all instances share the same network path
  (UNC/NFS). For multi-instance dev, use Azurite + the Azure provider instead.

- **Async all the way.** The library is `ConfigureAwait(false)` internally and is safe to call
  from classic ASP.NET, but callers should still expose `Task`-returning methods and never use
  `async void`.

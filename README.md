# RSK.FileManager

Unified file-storage abstraction for RSK applications. Consuming apps depend on a single
interface, `IFileManagerService`, backed by **Azure Blob Storage** or the **local File System**,
selected entirely by configuration. The same compiled app runs against the File System in
development and Azure Blob in UAT/Production with **no code changes** — only `appsettings` differs.

See `docs/RSK.FileManager-Architecture-v2.md` for the full design and
`docs/RSK.FileManager-Project-Plan.md` for the build plan.

## Packages

| Package | Purpose |
|---|---|
| `RSK.FileManager.Abstractions` | Interface, models, options, exceptions. Apps reference this. |
| `RSK.FileManager.AzureBlob` | Azure Blob provider (SAS / User Delegation SAS, Polly retry). |
| `RSK.FileManager.FileSystem` | File System provider (HMAC-signed secure URLs). |
| `RSK.FileManager` | `AddFileManager` (Core DI) + `FileManagerFactory` (net462); selects provider by config. |
| `RSK.FileManager.AspNetCore` | `MapFileManagerFileServer()` endpoint + health checks. |

## Target frameworks

`net462`, `netstandard2.0`, `net6.0`, `net8.0`. C# 12; `required`/`init` are polyfilled on
down-level TFMs by PolySharp (build-only).

## Build & test (Windows / Visual Studio 2022 or `dotnet` SDK 8)

```powershell
dotnet restore RSK.FileManager.sln
dotnet build   RSK.FileManager.sln -c Release      # builds all four TFMs
dotnet test    RSK.FileManager.sln                 # net8 unit/integration; net462 compat
```

> net462 targets require building on Windows. Integration tests against Azure Blob use
> **Azurite** (run `azurite` locally or via Docker).

## Status

Phase 1 in progress. Implemented so far (WP-00 → WP-04):
- Repository scaffold, multi-TFM build, PolySharp, analyzers, warnings-as-errors.
- `RSK.FileManager.Abstractions`: `IFileManagerService`, all models, options with fail-fast
  `Validate()`, exception hierarchy.
- Unit tests for the exception hierarchy, options validation (incl. the Managed-Identity
  never-expire rule), and `FileDownloadResult` disposal.

Next: `PathSanitizer` + `FileValidator` (WP-05/06), `FileSystemTokenService` (WP-07),
then the providers. See the project plan for the full work-package order.

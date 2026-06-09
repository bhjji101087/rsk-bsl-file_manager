# Changelog

All notable changes to RSK.FileManager are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/) and the project uses [SemVer](https://semver.org/).

## [1.0.0] — unreleased

Initial release.

### Added
- `IFileManagerService` contract with request/response models and a typed exception hierarchy
  (`RSK.FileManager.Abstractions`).
- Shared security primitives — `PathSanitizer`, `FileValidator` (size, extension whitelist,
  magic-byte content sniffing) — in `RSK.FileManager.Core`.
- **Azure Blob** provider: CRUD, single reused client, Service SAS (connection string) and
  cached User Delegation SAS (Managed Identity), Polly exponential-backoff retry
  (`RSK.FileManager.AzureBlob`).
- **File System** provider: CRUD, HMAC-signed secure URLs (path + expiry, constant-time
  comparison, URL-safe), opt-in empty-folder cleanup, resolved-path-under-root containment
  (`RSK.FileManager.FileSystem`).
- One-line registration: `AddFileManager` (.NET Core DI) and `FileManagerFactory`
  (.NET Framework) in `RSK.FileManager`.
- ASP.NET Core integration: `MapFileManagerFileServer()` and `AddFileManagerHealthCheck()`
  (`RSK.FileManager.AspNetCore`).
- Multi-targeting `net462; netstandard2.0; net6.0; net8.0` with PolySharp polyfills.
- Startup configuration validation (fail fast), including the Managed-Identity never-expire rule.
- Unit, integration (TestServer), and net462 compatibility tests, including a deadlock-regression
  test for the `ConfigureAwait(false)` guarantee.
- CI (build all TFMs, tests, coverage gate) and tag-triggered publish to GitHub Packages.

### Notes
- `Move`/`Copy` are implemented for the File System provider; the Azure provider throws
  `NotSupportedException` for these (planned for v1.1).

## Roadmap

- **v1.1** — Azure `Move`/`Copy`, soft delete, `IFileManagerTelemetry`, `BulkUploadAsync`.
- **v2.0** — Blob versioning, AWS S3 provider, chunked/resumable upload.

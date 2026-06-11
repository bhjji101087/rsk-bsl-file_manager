# RSK.FileManager — AI-Agent Build Plan

**Companion to:** `RSK.FileManager-Architecture-v2.md` (the architecture is the source of truth; this plan is the execution order.)
**Audience:** an autonomous AI coding agent (and human reviewers).
**Goal:** build, test and package `RSK.FileManager` v1.0.0 from an empty repository, one work package at a time.

---

## 0. Agent Operating Instructions  *(read fully before WP-00)*

### 0.1 How to use this plan
- Execute **work packages (WP-xx) in ascending order**. Each WP lists `Depends on`. Do not start a WP until its dependencies are `DONE`.
- A WP is **DONE** only when every item in its *Acceptance Criteria* passes, its tests are green, and the solution still builds for **all four TFMs** with warnings-as-errors.
- After each WP: run `dotnet build` and `dotnet test`, then commit with the message `feat(WP-xx): <subject>`. Never proceed on a red build.
- If a WP is blocked or ambiguous, stop and emit a short note describing the blocker and your proposed resolution; do not guess on security-relevant behavior (SAS, HMAC, path sanitization).
- Treat the architecture doc sections (e.g. "§11.3") as the detailed spec. This plan references them rather than repeating them.

### 0.2 Tech stack (fixed)
- C# 12, `LangVersion=latest`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`.
- TFMs: `net462;netstandard2.0;net6.0;net8.0`.
- Test stack: **xUnit** + **FluentAssertions** + **NSubstitute** (mocks only at your own seams) + **Coverlet** (coverage).
- Integration: **Azurite** via Docker for Blob; `Path.GetTempPath()` for FS.
- Packages: `Azure.Storage.Blobs` 12.x, `Polly` 8.x, `Microsoft.Extensions.*` (Logging.Abstractions, Options, DependencyInjection.Abstractions), `PolySharp` (build-only).

### 0.3 Non-negotiable guardrails  *(a PR that violates any of these is rejected)*
1. **`ConfigureAwait(false)` on every `await`** in `src/**`. CI runs an analyzer that fails the build otherwise.
2. **No `async void`** (except UI handlers — there are none here). No `.Result` / `.Wait()` / `.GetAwaiter().GetResult()` in `src/**`.
3. **Apps depend only on `RSK.FileManager.Abstractions`.** No `src` project except the providers references `Azure.Storage.Blobs`. The Abstractions project references **only** `Microsoft.Extensions.Logging.Abstractions`.
4. **Security code is spec-exact.** `PathSanitizer`, `FileValidator`, `FileSystemTokenService`, and SAS generation must match §11–§12 precisely, including `CryptographicOperations.FixedTimeEquals`, URL-safe Base64, and signing **path + expiry**.
5. **Fail fast.** All config errors throw `FileManagerConfigException` at startup (`Validate()`), never at first call.
6. **Singleton storage client.** Construct `BlobContainerClient` once per provider instance; never inside an operation method.
7. **Public API has XML docs.** `GenerateDocumentationFile=true`; missing docs on public members fail the build.
8. **No secrets in code or tests.** Use Azurite's well-known dev connection string and a test-only HMAC secret ≥ 32 chars.

### 0.4 Definition of Done (per WP) — checklist the agent self-verifies
- [ ] Code compiles for all 4 TFMs, warnings-as-errors clean.
- [ ] New/changed public members have XML docs.
- [ ] Unit tests added and green; coverage for the WP's code ≥ 85%.
- [ ] No guardrail (0.3) violated.
- [ ] Architecture references for the WP implemented exactly.
- [ ] Committed as `feat(WP-xx): …`.

### 0.5 Naming map (do not deviate)
| Concept | Name |
|---|---|
| Interface | `IFileManagerService` |
| Options root | `FileManagerOptions` |
| Config section | `FileManager` |
| Provider enum | `StorageProvider { AzureBlob, FileSystem }` |
| Base exception | `FileManagerException` |
| DI entry | `AddFileManager(IConfiguration)` |
| net462 entry | `FileManagerFactory.Initialize(...)` / `.GetService()` |
| FS endpoint | `app.MapFileManagerFileServer()` |

---

## Milestone M1 — Repository & Build Scaffold

### WP-00 — Solution, repo layout, shared build props
**Depends on:** none
**Goal:** create the empty multi-package solution that builds for all TFMs.
**Files:**
- `RSK.FileManager.sln`
- `build/Directory.Build.props` (TFMs, Nullable, ImplicitUsings, LangVersion, TreatWarningsAsErrors, GenerateDocumentationFile, PolySharp `PrivateAssets=all`) — per Architecture §18.
- `.editorconfig` (enforce analyzer rules incl. ConfigureAwait), `.gitignore` (VS/.NET).
- Empty `src/` projects: `RSK.FileManager.Abstractions`, `RSK.FileManager.AzureBlob`, `RSK.FileManager.FileSystem`, `RSK.FileManager`, `RSK.FileManager.AspNetCore` (last is `net6.0;net8.0` only).
- Empty `tests/` projects: UnitTests, IntegrationTests, CompatTests (`net462`).
- Project references per the dependency graph in Architecture §4.
**Acceptance criteria:**
- `dotnet build RSK.FileManager.sln` succeeds for all TFMs.
- `RSK.FileManager.Abstractions` references only `Microsoft.Extensions.Logging.Abstractions`.
- Only `RSK.FileManager.AzureBlob` references `Azure.Storage.Blobs` and `Polly`.
- `RSK.FileManager.AspNetCore` does not target net462/netstandard2.0.
**Tests:** a trivial `SmokeTest` asserting `true` in each test project, all green.

### WP-01 — Polyfills & analyzer enforcement
**Depends on:** WP-00
**Goal:** make `required`/`init` compile down-level and wire the ConfigureAwait analyzer.
**Files:** confirm `PolySharp` in Directory.Build.props (Architecture §18); add the ConfigureAwait analyzer package + `.editorconfig` rule severity = error.
**Acceptance criteria:**
- A scratch class using `public required string X { get; init; }` compiles on net462 & netstandard2.0.
- An intentionally context-capturing `await` in a temporary file fails the build; remove the scratch after verifying.

---

## Milestone M2 — Abstractions (the contract)

### WP-02 — Exceptions
**Depends on:** WP-01
**Goal:** exception hierarchy per Architecture §16.
**Files:** `Exceptions/FileManager*.cs` (`FileManagerException` base + `NotFound`, `Validation`, `Provider`, `Transient : Provider`, `Config`).
**Acceptance criteria:** each is `[Serializable]`, has the three standard ctors (message; message+inner; default), and the hierarchy matches §16.
**Tests:** `ExceptionHierarchyTests` — `FileManagerTransientException` is a `FileManagerProviderException` is a `FileManagerException`.

### WP-03 — Options + startup validation
**Depends on:** WP-02
**Goal:** `FileManagerOptions`, `AzureBlobOptions`, `FileSystemOptions`, `StorageProvider`, with `Validate()` exactly per Architecture §8.
**Files:** `Options/*.cs`.
**Acceptance criteria (validation must throw `FileManagerConfigException` for):**
- `DefaultSecureUrlExpiryHours < 0`.
- `MaxFileSizeBytes <= 0`.
- AzureBlob + `UseManagedIdentity=true` + missing `AccountName`.
- AzureBlob + `UseManagedIdentity=true` + `DefaultSecureUrlExpiryHours == 0` (never-expire not allowed with MSI).
- AzureBlob + `UseManagedIdentity=true` + `DefaultSecureUrlExpiryHours > 168`.
- AzureBlob + `UseManagedIdentity=false` + empty `ConnectionString`.
- AzureBlob + empty `RootContainer`.
- FileSystem + empty `RootPath`/`ServeBaseUrl`.
- FileSystem + `TokenSecret` shorter than 32 chars or starting with `REPLACE_WITH`.
- Valid configs do **not** throw.
**Tests:** `FileManagerOptionsValidationTests` — one case per bullet above (theory).

### WP-04 — Models + IFileManagerService
**Depends on:** WP-03
**Goal:** request/response models and the interface, per Architecture §6–§7.
**Files:** `Models/Request/FileUploadRequest.cs`, `Models/Response/{FileUploadResult,FileDownloadResult,FileMetadata,FileListResult,SecureUrlResult}.cs`, `IFileManagerService.cs`.
**Acceptance criteria:**
- `FileDownloadResult` implements both `IAsyncDisposable` and `IDisposable`, disposing `Content`.
- `SecureUrlResult.ExpiresAt` is nullable (`DateTimeOffset?`).
- Interface signatures match §6 exactly (incl. `CancellationToken` defaults).
- No provider/Azure types leak into Abstractions.
**Tests:** `FileDownloadResultTests` — disposing the result disposes the underlying stream (use a tracking stream).

---

## Milestone M3 — Security primitives  *(highest scrutiny)*

### WP-05 — PathSanitizer
**Depends on:** WP-04
**Goal:** `PathSanitizer.Sanitize(path)` + `Validate(path)` per Architecture §12.1.
**Files:** `src/RSK.FileManager/Security/PathSanitizer.cs`.
**Acceptance criteria:** rejects `..` traversal, null bytes, control chars, > 250 chars; strips leading `/ \ .`; normalizes to `/`; trims; throws `FileManagerValidationException` on violation.
**Tests:** `PathSanitizerTests` with an attack-vector theory: `../../etc/passwd`, `..\\..\\windows\\system32`, `foo/../../bar`, `a\0b`, leading slashes, 300-char path, mixed separators, valid nested path passes and normalizes.

### WP-06 — FileValidator (size, extension, magic-byte sniffing)
**Depends on:** WP-05
**Goal:** `ValidateSize`, `ValidateExtension`, and **`ValidateContentAsync`** per Architecture §12.2.
**Files:** `src/RSK.FileManager/Security/FileValidator.cs`.
**Acceptance criteria:**
- Size over `MaxFileSizeBytes` → throws.
- Extension not in non-empty `AllowedExtensions` → throws; empty list → allow any.
- Content sniffing (when enabled): reads leading bytes; rejects mismatched signature for known types (`%PDF`, PNG, JPEG, PK/zip); hard-blocks `MZ` executables regardless of name; **rewinds** the stream (`Seek(0)`) so the body is intact; buffers the header for non-seekable streams.
**Tests:** `FileValidatorTests` — renamed `.exe` (MZ bytes) as `report.pdf` is rejected; valid `%PDF` passes; oversize rejected; extension whitelist enforced; stream position is 0 after sniffing.

### WP-07 — FileSystemTokenService (HMAC)
**Depends on:** WP-05
**Goal:** HMAC-SHA256 token create/validate per Architecture §11.4.
**Files:** `src/RSK.FileManager.FileSystem/Security/FileSystemTokenService.cs`.
**Acceptance criteria:**
- Signs **path + expiry** (path-only when never-expire); unambiguous separator.
- `Validate` uses `CryptographicOperations.FixedTimeEquals`.
- Tokens are URL-safe Base64 (round-trips).
- Expired token → invalid; tampered path → invalid; swapping a token from file A to file B → invalid.
**Tests:** `FileSystemTokenServiceTests` — valid round-trip; path-swap rejected; expiry-in-the-past rejected; never-expire token validates; URL-safe charset contains no `+ / =`.

---

## Milestone M4 — FileSystem provider (end-to-end on the simpler backend first)

### WP-08 — FileManagerProviderBase + FileSystemProvider
**Depends on:** WP-06, WP-07
**Goal:** shared base (Architecture §17) and the full FS provider (Architecture §13.2): upload (stream + byte[]), download (`FileDownloadResult`), delete (with **opt-in** `RemoveEmptyFolders`), exists, metadata, list, GetSecureUrl (HMAC). Auto folder creation via `Directory.CreateDirectory`. `FileShare.Read` on reads.
**Files:** `src/RSK.FileManager/Providers/Base/FileManagerProviderBase.cs`, `src/RSK.FileManager.FileSystem/FileSystemProvider.cs`.
**Acceptance criteria:**
- Every `await` has `ConfigureAwait(false)`.
- Upload runs the §17 validation pipeline (path, size, extension, content sniffing) before writing.
- `DeleteAsync` removes empty parents **only when** `RemoveEmptyFolders=true`, walking up to `RootPath`.
- `GetSecureUrlAsync` builds `{ServeBaseUrl}/{path}?expires=&token=` honoring the §11.1 expiry rule.
- Download returns content type + size in `FileDownloadResult`.
**Tests:** unit tests with a temp directory: lifecycle upload→exists→metadata→list→download→delete; overwrite=false throws on existing; opt-in folder cleanup on/off; never-expire URL omits `expires`.

---

## Milestone M5 — Azure Blob provider

### WP-09 — AzureBlobProvider core operations
**Depends on:** WP-08
**Goal:** Azure provider CRUD per Architecture §13.1: singleton `BlobContainerClient`, upload/download/delete/exists/metadata/list, MSI vs connection-string client construction.
**Files:** `src/RSK.FileManager.AzureBlob/AzureBlobProvider.cs`.
**Acceptance criteria:**
- `BlobContainerClient` created once in the ctor; never inside a method.
- Connection-string → `BlobServiceClient(connStr)`; MSI → `BlobServiceClient(uri, DefaultAzureCredential)`.
- Same §17 validation pipeline on upload.
- `DownloadAsync` populates `FileDownloadResult` from the blob response (content type, length, metadata) — no second round-trip.
**Tests (integration, Azurite):** lifecycle as in WP-08; prefix "folders"; overwrite behavior; metadata round-trip.

### WP-10 — Polly retry
**Depends on:** WP-09
**Goal:** `RetryPolicies` per Architecture §13.1 — exponential backoff over transient `RequestFailedException` (429/500/503); surface exhausted failures as `FileManagerTransientException`.
**Files:** `src/RSK.FileManager.AzureBlob/Internal/RetryPolicies.cs`; wire into provider operations.
**Acceptance criteria:** `RetryMaxAttempts`/`RetryBaseDelaySeconds` honored; non-transient errors not retried.
**Tests:** simulate transient failures (a fake pipeline/transport returning 503 then 200) and assert N retries then success; assert a 404 is not retried.

### WP-11 — SAS generation (both modes)
**Depends on:** WP-09
**Goal:** `GetSecureUrlAsync` for Azure per Architecture §11.2–§11.3.
**Files:** extend `AzureBlobProvider`.
**Acceptance criteria:**
- Connection-string path → **Service SAS** via `GenerateSasUri`, Read-only; never-expire → far-future `ExpiresOn`.
- MSI path → **User Delegation SAS**: fetch delegation key, **cache and reuse** it until near expiry, sign with `ToSasQueryParameters(key, accountName)`; Read-only.
- Expiry rule (§11.1) applied; `SecureUrlResult.ExpiresAt` set (or null).
**Tests:** unit — auth-mode selection picks the correct path; never-expire under MSI is already blocked at config (WP-03), so assert provider assumes a bounded expiry. Integration (Azurite supports SAS) — generated URL downloads the blob; expired URL is rejected.

---

## Milestone M6 — Registration glue

### WP-12 — AddFileManager (.NET Core DI)
**Depends on:** WP-11
**Goal:** `AddFileManager(IConfiguration)` per Architecture §9 — bind, `Validate()` (fail fast), register provider as **singleton** with provider-specific `ILogger<T>`.
**Files:** `src/RSK.FileManager/Extensions/ServiceCollectionExtensions.cs`.
**Acceptance criteria:** missing section → `FileManagerConfigException`; provider resolved per `Provider`; logger category is the concrete provider type.
**Tests:** build a `ServiceProvider` from in-memory config for each provider; resolve `IFileManagerService`; assert correct concrete type; bad config throws at registration.

### WP-13 — FileManagerFactory (.NET Framework, no DI)
**Depends on:** WP-11
**Goal:** `FileManagerFactory.Initialize(NameValueCollection)` + `GetService()` per Architecture §10 — parse flat `FileManager:*` keys, validate, build & cache singleton.
**Files:** `src/RSK.FileManager/Legacy/FileManagerFactory.cs`.
**Acceptance criteria:** `GetService()` before `Initialize()` throws a clear error; repeated `GetService()` returns the same instance; invalid keys → `FileManagerConfigException`.
**Tests (in CompatTests, net462):** initialize from a `NameValueCollection`; resolve FS provider; round-trip an upload/download in a temp dir; assert no deadlock when the call is blocked synchronously.

---

## Milestone M7 — ASP.NET Core integration

### WP-14 — MapFileManagerFileServer endpoint
**Depends on:** WP-12
**Goal:** the file-serving endpoint per Architecture §11.4 — `GET {ServeBaseUrl}/{*path}?expires=&token=`; validate token via `FileSystemTokenService`; **re-sanitize path**; stream the file; 404 on missing, 403 on bad/expired token.
**Files:** `src/RSK.FileManager.AspNetCore/FileManagerEndpointExtensions.cs`.
**Acceptance criteria:** valid token streams the file with correct content type; tampered/expired token → 403; traversal in path → 400/403; only serves within `RootPath`. No-op safe when `Provider=AzureBlob`.
**Tests:** `WebApplicationFactory` integration — happy path 200; tampered token 403; `..` path rejected.

### WP-15 — Health check
**Depends on:** WP-14
**Goal:** `AddFileManagerHealthCheck()` per Architecture §23.9 — Blob: container reachable; FS: root writable.
**Files:** `src/RSK.FileManager.AspNetCore/FileManagerHealthCheck.cs`.
**Acceptance criteria:** returns Healthy/Unhealthy appropriately; registered into the standard health-check pipeline.
**Tests:** FS health check against a temp dir (healthy) and a non-writable/non-existent path (unhealthy).

---

## Milestone M8 — Test hardening & coverage

### WP-16 — Coverage gate, attack-vector sweep, compat
**Depends on:** WP-15
**Goal:** reach ≥ 85% line coverage; expand security tests; confirm net462 behavior.
**Acceptance criteria:**
- Coverlet reports ≥ 85% across `src`.
- Security suites (PathSanitizer, FileValidator, TokenService, SAS selection) cover the §24 risks.
- CompatTests prove async-call-from-sync-context does not deadlock (`ConfigureAwait(false)` guarantee).
**Tests:** the consolidated suites above; a deadlock-regression test that blocks on `DownloadAsync().GetAwaiter().GetResult()` from a captured-context test host and completes within a timeout.

---

## Milestone M9 — Packaging, CI/CD, docs, samples

### WP-17 — NuGet metadata & local pack
**Depends on:** WP-16
**Goal:** per-package metadata per Architecture §19; produce `.nupkg` + `.snupkg` locally.
**Acceptance criteria:** `dotnet pack -c Release` produces all five packages with README, symbols, XML docs; dependency graph matches §4 (Abstractions has no Azure dep).

### WP-18 — CI pipeline
**Depends on:** WP-17
**Goal:** `ci.yml` per Architecture §22 — restore, build all TFMs (warnings-as-errors), unit + integration (Azurite service container), coverage ≥ 85%, vulnerability scan, ConfigureAwait analyzer, pack dry-run.
**Acceptance criteria:** pipeline green on a PR; coverage and analyzer gates actually fail the build when violated (prove with a temporary breaking commit, then revert).

### WP-19 — publish.yml
**Depends on:** WP-18
**Goal:** tag-triggered (`v*`) pack + push to RSK Azure Artifacts + GitHub release (Architecture §22).
**Acceptance criteria:** dry-run on a pre-release tag pushes to a test feed; release notes pull from CHANGELOG.

### WP-20 — Docs & samples
**Depends on:** WP-17
**Goal:** `README.md`, `docs/CONFIGURATION.md`, `docs/MIGRATION.md`, `docs/CHANGELOG.md`; `samples/RSK.FileManager.Sample.NetCore` (ASP.NET Core 8, FS in dev / Blob in prod via config) and `samples/RSK.FileManager.Sample.NetFramework` (net462 + factory).
**Acceptance criteria:**
- README shows the one-line `AddFileManager` + `MapFileManagerFileServer` and the FS↔Blob switch-by-config story.
- MIGRATION.md gives the step list to replace an existing `IFileService` (Architecture §20-equivalent) and **states the delete-authorization caveat** (§12.3) and the **MSI never-expire** rule (§11).
- CONFIGURATION.md documents every key incl. the expiry rule table (§11.1).
- Both samples run.

---

## Execution Order (dependency-sorted)

```
WP-00 → WP-01 → WP-02 → WP-03 → WP-04            (scaffold + contract)
                         → WP-05 → WP-06         (path sanitizer, file validator)
                         → WP-05 → WP-07         (HMAC token service)
WP-06,WP-07 → WP-08                              (FileSystem provider)
WP-08 → WP-09 → WP-10                            (Azure provider + retry)
        WP-09 → WP-11                            (SAS, both modes)
WP-11 → WP-12 (Core DI)
WP-11 → WP-13 (net462 factory)
WP-12 → WP-14 → WP-15                            (AspNetCore endpoint + health)
WP-15 → WP-16                                    (coverage/security/compat)
WP-16 → WP-17 → WP-18 → WP-19                    (pack + CI + publish)
WP-17 → WP-20                                    (docs + samples)
```

Parallelizable once WP-04 is done: the security primitives (WP-05/06/07) and, after WP-08, the Azure track (WP-09→11) and the registration track can progress with light coordination. A single agent should follow the linear order above.

---

## Final Acceptance Checklist (v1.0.0 ship gate)

- [ ] All 21 work packages DONE.
- [ ] `dotnet build -c Release` clean for net462/netstandard2.0/net6.0/net8.0, warnings-as-errors.
- [ ] `dotnet test` all green; coverage ≥ 85%.
- [ ] Guardrails (0.3) hold: ConfigureAwait analyzer green; Abstractions has no Azure dependency; no `async void`/`.Result` in `src`.
- [ ] Security: PathSanitizer attack-vector suite, FileValidator magic-byte suite, HMAC path-swap/timing/URL-safe suite, SAS auth-mode suite — all green.
- [ ] Config fail-fast verified, including MSI-never-expire rejection and weak-secret rejection.
- [ ] FS↔Blob switch proven by config in the NetCore sample with no code change.
- [ ] net462 sample runs via `FileManagerFactory`; deadlock-regression test passes.
- [ ] Five NuGet packages pack with README/symbols/XML docs and correct dependency graph.
- [ ] CI green; publish.yml dry-run pushes to a test feed.
- [ ] README, CONFIGURATION, MIGRATION, CHANGELOG complete.

---

*This plan is intended to be executed top-to-bottom by an AI coding agent, committing after each work package and never proceeding on a red build. The architecture document is authoritative for any detail not fully specified here.*

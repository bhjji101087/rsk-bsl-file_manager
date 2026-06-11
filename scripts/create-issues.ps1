# Creates labels and the remaining-work backlog as GitHub issues via the GitHub CLI.
# Run once from the repo root:  pwsh ./scripts/create-issues.ps1
# Requires: gh auth login (with repo scope).

$ErrorActionPreference = 'Stop'
$repo = 'bhjji101087/rsk-bsl-file_manager'

function New-Label($name, $color, $desc) {
    gh label create $name --color $color --description $desc --repo $repo --force | Out-Null
    Write-Host "label: $name"
}

New-Label 'phase-1'    '0e8a16' 'v1.0 scope'
New-Label 'phase-2'    'fbca04' 'v1.1 scope'
New-Label 'phase-3'    'd93f0b' 'v2.0 scope'
New-Label 'follow-up'  '5319e7' 'Follow-up / tech debt'
New-Label 'ci'         'c5def5' 'CI/CD'
New-Label 'tests'      'bfd4f2' 'Test coverage'

function New-Issue($title, $labels, $body) {
    gh issue create --repo $repo --title $title --label $labels --body $body | Out-Null
    Write-Host "issue: $title"
}

New-Issue 'Verify first green CI run on development' 'ci,phase-1' `
    "After merging the foundation PR, confirm ci.yml builds all four TFMs (net462/netstandard2.0/net6.0/net8.0) with warnings-as-errors and that unit/integration/compat tests pass. This is the first real compile/test verification of the codebase. Watch the Azure SAS unit tests and the ASP.NET Core TestServer tests."

New-Issue 'Azurite-backed Azure Blob integration tests' 'tests,phase-1,follow-up' `
    "Add full lifecycle integration tests (upload/list/download/delete, SAS download, retry on simulated transient) for AzureBlobProvider against Azurite. CI already starts Azurite; gate the tests on its availability."

New-Issue 'Azure: implement Move/Copy (server-side blob copy)' 'phase-2' `
    "AzureBlobProvider.MoveAsync/CopyAsync currently throw NotSupportedException. Implement server-side copy (StartCopyFromUri/SyncCopy) with same-account auth; Move = copy + delete source. FileSystem provider already implements these."

New-Issue 'Azure soft delete option' 'phase-2' `
    "Add a SoftDelete option; DeleteAsync currently hard-deletes. Recommend enabling 30-day soft delete on the storage account and exposing recovery."

New-Issue 'IFileManagerTelemetry hook' 'phase-2' `
    "Introduce an IFileManagerTelemetry abstraction so consumers can plug Application Insights custom events without coupling the package to a telemetry provider."

New-Issue 'BulkUploadAsync with concurrency control' 'phase-2' `
    "Add BulkUploadAsync(IEnumerable<...>) with a configurable SemaphoreSlim degree of parallelism for batch uploads; respect Azure throttling."

New-Issue 'Blob versioning (GetVersionsAsync)' 'phase-3' `
    "Expose Azure Blob versioning for document-management scenarios."

New-Issue 'AWS S3 provider' 'phase-3' `
    "Add RSK.FileManager.S3 implementing IFileManagerService, selected by config like the existing providers."

New-Issue 'Chunked / resumable upload' 'phase-3' `
    "Support chunked/resumable uploads for large files (staged block upload on Azure)."

Write-Host "Done. View: gh issue list --repo $repo"

# Create a feature branch off development, commit the current changes,
# push, and open a PR into development.
#
# Example:
#   pwsh ./scripts/new-feature.ps1 `
#     -Branch  "feat/wp-08-filesystem-provider" `
#     -Message "feat(WP-08): FileManagerProviderBase + FileSystemProvider" `
#     -PrTitle "feat(WP-08): FileManagerProviderBase + FileSystemProvider" `
#     -PrBody  "Implements arch §13.2/§17. Tests over a temp directory."

param(
    [Parameter(Mandatory = $true)] [string] $Branch,
    [Parameter(Mandatory = $true)] [string] $Message,
    [Parameter(Mandatory = $true)] [string] $PrTitle,
    [Parameter(Mandatory = $false)][string] $PrBody = ''
)

$ErrorActionPreference = 'Stop'

Write-Host "==> Updating development..." -ForegroundColor Cyan
git checkout development
git pull --ff-only origin development

Write-Host "==> Creating branch $Branch" -ForegroundColor Cyan
git checkout -b $Branch

Write-Host "==> Committing changes" -ForegroundColor Cyan
git add -A
git commit -m $Message

Write-Host "==> Pushing $Branch" -ForegroundColor Cyan
git push -u origin $Branch

$gh = Get-Command gh -ErrorAction SilentlyContinue
if ($gh) {
    Write-Host "==> Opening PR into development" -ForegroundColor Cyan
    gh pr create --base development --head $Branch --title $PrTitle --body $PrBody
} else {
    $compare = "https://github.com/bhjji101087/rsk-bsl-file_manager/compare/development...$Branch?expand=1"
    Write-Host "GitHub CLI (gh) not found. Open the PR manually:" -ForegroundColor Yellow
    Write-Host "  $compare" -ForegroundColor Yellow
}

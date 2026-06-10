# One-time Git setup for RSK.FileManager.
# Cleans the partial repo created in the sandbox, wires the GitHub remote,
# and bases the working tree on the existing 'development' branch.
#
# Run from the repository root:  pwsh ./scripts/setup-git.ps1

$ErrorActionPreference = 'Stop'
$RepoUrl = 'https://github.com/bhjji101087/rsk-bsl-file_manager.git'

Write-Host '==> Removing any partial .git directory...' -ForegroundColor Cyan
if (Test-Path '.git') { Remove-Item -Recurse -Force '.git' }

Write-Host '==> git init' -ForegroundColor Cyan
git init | Out-Null

Write-Host '==> Configure remote' -ForegroundColor Cyan
git remote add origin $RepoUrl 2>$null
git remote set-url origin $RepoUrl

Write-Host '==> Fetch development' -ForegroundColor Cyan
git fetch origin

# Base the local branch on the existing remote development branch.
# Working-tree files (your code) stay in place as untracked changes,
# ready to be committed onto a feature branch.
git checkout -B development origin/development

Write-Host ''
Write-Host 'Setup complete. You are on "development" tracking origin/development.' -ForegroundColor Green
Write-Host 'Next: create a feature branch and PR with scripts/new-feature.ps1' -ForegroundColor Green

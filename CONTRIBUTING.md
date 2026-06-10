# Contributing — branching, commits & PRs

Remote: `https://github.com/bhjji101087/rsk-bsl-file_manager`
Default integration branch: **`development`** (all PRs target this).

## Branch naming

One branch per work package / change, branched from `development`:

```
feat/wp-08-filesystem-provider
feat/wp-09-azureblob-provider
fix/wp-06-sniffing-rewind
chore/ci-coverage-gate
docs/configuration-guide
```

Format: `<type>/wp-<nn>-<short-slug>` (type = feat | fix | chore | docs | test | refactor).

## Commit messages (Conventional Commits)

```
<type>(WP-08): <imperative summary>

<body: what & why, bullet points ok>
```

Example:

```
feat(WP-08): add FileManagerProviderBase and FileSystemProvider

- Shared upload validation pipeline (path, size, extension, magic-byte sniffing)
- Full FileSystem CRUD + HMAC secure URLs, opt-in empty-folder cleanup
- Resolved-path-under-root containment check
- Unit tests over a temp directory
```

## Pull requests

- Base branch: `development`. One PR per branch/WP.
- Title mirrors the commit summary: `feat(WP-08): FileManagerProviderBase + FileSystemProvider`.
- Body: what changed, which architecture sections it implements (e.g. §13.2, §17), and the test evidence.
- Keep the branch green: `dotnet build -c Release` and `dotnet test` must pass before opening the PR.

## One-time setup

Run `scripts/setup-git.ps1` once (it cleans the partial repo, wires the remote, and bases you on `development`).

## Per work-package flow

Use the helper:

```powershell
scripts/new-feature.ps1 `
  -Branch "feat/wp-08-filesystem-provider" `
  -Message "feat(WP-08): FileManagerProviderBase + FileSystemProvider`n`n- shared validation pipeline`n- full FileSystem CRUD + HMAC URLs" `
  -PrTitle "feat(WP-08): FileManagerProviderBase + FileSystemProvider" `
  -PrBody  "Implements arch §13.2 and §17. Unit tests over a temp directory. Build + tests green."
```

The script creates the branch off `development`, commits, pushes, and opens the PR (via `gh`).
If you don't have the GitHub CLI, it stops after pushing and prints the PR URL to open in the browser.

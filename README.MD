# PSMaintenance

Rich documentation viewer/installer for PowerShell modules.

- Render README, CHANGELOG, License, Intro/Upgrade text, Internals\Docs as tabs
- Parse Get-Help to pretty HTML with syntax, parameter sets, examples
- Show dependencies (table + diagram) and commands
- Fetch docs directly from repository (GitHub/Azure DevOps), including Docs folders

## Install

```powershell
Install-Module PSMaintenance -Scope CurrentUser
```

## Quick start

```powershell
# View docs (local only)
Show-ModuleDocumentation -Name 'EFAdminManager'

# Include repository docs and standard tabs (README/CHANGELOG/LICENSE)
Show-ModuleDocumentation -Name 'EFAdminManager' -Online

# Copy bundled docs (Internals + README/CHANGELOG/LICENSE) to a folder
Install-ModuleDocumentation -Name 'EFAdminManager' -Path 'C:\Docs' -Layout ModuleAndVersion

# Copy only scripts (no versioned folder), and unblock on Windows
Install-ModuleScript -Name 'EFAdminManager' -Path 'C:\Tools' -Unblock -Verbose
```

## Configure repository access (private repos)

PSMaintenance can fetch documentation directly from your repository defined in the module manifest (`PrivateData.PSData.ProjectUri`). For private repositories, store a token once:

- GitHub
  - Token scope: `repo` (read access)
  - Windows: `setx PG_GITHUB_TOKEN "ghp_xxx"`
  - Or persist securely: `Set-ModuleDocumentation -GitHubToken 'ghp_xxx'`

- Azure DevOps
  - URL: `https://dev.azure.com/{organization}/{project}/_git/{repository}`
  - PAT scope: `Code (Read)`
  - Windows: `setx PG_AZDO_PAT "your_pat"`
  - Or persist securely: `Set-ModuleDocumentation -AzureDevOpsPat 'your_pat'`

You can also pass a token per-call: `-RepositoryToken '...'`. Stored tokens are used when env vars and explicit tokens are not provided.

Examples:

```powershell
# Store tokens explicitly
Set-ModuleDocumentation -GitHubToken 'ghp_xxx'
Set-ModuleDocumentation -AzureDevOpsPat 'azdopat_xxx'

# Read from environment variables
$env:PG_GITHUB_TOKEN='ghp_xxx'; $env:PG_AZDO_PAT='azdopat_xxx'
Set-ModuleDocumentation -FromEnvironment

# Remove stored tokens
Set-ModuleDocumentation -Clear
```

## Tell PSMaintenance where your Docs live in the repo

Add runtime hints in your module manifest (.psd1):

```powershell
PrivateData = @{
  PSData = @{
    ProjectUri = 'https://dev.azure.com/Org/Project/_git/Repo'  # or GitHub URL
    Delivery   = @{ InternalsPath = 'Internals'; DocsPaths = @('Internals\Docs','Docs') }
    Repository = @{ Branch = 'main'; Paths = @('Docs','README.md','CHANGELOG.md') }
  }
}
```



Then:

```powershell
Show-ModuleDocumentation -Name 'EFAdminManager' -Online
Show-ModuleDocumentation -Name 'EFAdminManager' -Online -Mode All
Show-ModuleDocumentation -Name 'EFAdminManager' -Online -Mode PreferRemote -RepositoryPaths 'Docs/en-US'
```

## Commands

- `Show-ModuleDocumentation` [Alias: `Show-Documentation`]
  - Displays README, CHANGELOG, LICENSE, Intro/Upgrade, Docs; local or `-Online` from repository.
- `Install-ModuleDocumentation` [Alias: `Install-Documentation`]
  - Copies Internals and selected root docs to a destination path.
- `Install-ModuleScript` [Alias: `Install-ModuleScripts`, `Install-Scripts`]
  - Copies only `.ps1` from `Internals\Scripts` to a target folder. Supports `-Include`, `-Exclude`, `-OnExists`, and `-Unblock`.
- `Set-ModuleDocumentation` [Alias: `Set-Documentation`]
  - Stores or clears tokens used to access private GitHub/Azure DevOps repos.

## Security notes

- On Windows, persisted tokens are protected with DPAPI (CurrentUser scope).
- On non-Windows, tokens are stored as Base64 (best effort). Prefer environment variables if you require stronger guarantees.

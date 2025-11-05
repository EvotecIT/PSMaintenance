Import-Module "$PSScriptRoot\..\PSMaintenance.psd1" -Force

# Store tokens explicitly
Set-ModuleDocumentation -GitHubToken 'ghp_xxx' -Verbose
Set-ModuleDocumentation -AzureDevOpsPat 'azdopat_xxx' -Verbose

# Read tokens from environment variables (preferred for CI)
$env:PG_GITHUB_TOKEN = 'ghp_xxx'
$env:PG_AZDO_PAT     = 'azdopat_xxx'
Set-ModuleDocumentation -FromEnvironment -Verbose

# Clear any stored tokens
Set-ModuleDocumentation -Clear -Verbose


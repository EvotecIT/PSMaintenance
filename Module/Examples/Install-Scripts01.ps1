Import-Module PSMaintenance -Force

# Copy only .ps1 files from Internals\Scripts of the installed module
$dest = Join-Path $env:TEMP 'EFAdminManager-Scripts'
Install-ModuleScript -Name 'EFAdminManager' -Path $dest -OnExists Merge -Unblock -Verbose

# Copy a subset (only repair scripts), skip existing files
# Install only repair scripts, skip existing
# Install-ModuleScript -Name 'EFAdminManager' -Path $dest -Include 'Repair-*' -OnExists Skip -Verbose

# Preview planned actions without copying
# Get-Module -ListAvailable 'EFAdminManager' | Install-ModuleScript -Path $dest -ListOnly

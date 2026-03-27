$moduleRoot = $PSScriptRoot
$manifestPath = Join-Path $moduleRoot 'PSMaintenance.psd1'

if (-not (Test-Path $manifestPath)) {
    throw "Module manifest not found at $manifestPath"
}

$requiredModules = @('Pester')
foreach ($requiredModule in $requiredModules) {
    if (-not (Get-Module -ListAvailable -Name $requiredModule)) {
        Install-Module -Name $requiredModule -Repository PSGallery -Force -SkipPublisherCheck -AllowClobber
    }
}

Import-Module Pester -Force -ErrorAction Stop

$configuration = [PesterConfiguration]::Default
$configuration.Run.Path = (Join-Path $moduleRoot 'Tests')
$configuration.Run.Exit = $true
$configuration.Should.ErrorAction = 'Continue'
$configuration.CodeCoverage.Enabled = $false
$configuration.Output.Verbosity = 'Detailed'

$result = Invoke-Pester -Configuration $configuration
if ($result.FailedCount -gt 0) {
    throw "$($result.FailedCount) tests failed."
}

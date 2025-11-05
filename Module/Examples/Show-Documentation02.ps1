Import-Module "$PSScriptRoot\..\PSMaintenance.psd1" -Force

# Include repository docs and open combined HTML
Show-ModuleDocumentation -Name 'PSPublishModule' -Online

$moduleRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $moduleRoot 'PSMaintenance.psd1'
Describe 'PSMaintenance module' {
    BeforeAll {
        $script:expectedCmdlets = @(
            'Get-ModuleDocumentation'
            'Install-ModuleScript'
            'Install-ModuleDocumentation'
            'Set-ModuleDocumentation'
            'Show-ModuleDocumentation'
        )
        $script:expectedAliases = @(
            'Install-Documentation'
            'Install-ModuleScripts'
            'Install-Scripts'
            'Set-Documentation'
            'Show-Documentation'
        )
        $manifest = Import-PowerShellDataFile -Path $manifestPath
        $module = Import-Module $manifestPath -Force -PassThru -ErrorAction Stop
    }

    AfterAll {
        Remove-Module PSMaintenance -Force -ErrorAction SilentlyContinue
    }

    It 'loads the module manifest' {
        $manifest.RootModule | Should -Be 'PSMaintenance.psm1'
        $manifest.CmdletsToExport | Should -Not -BeNullOrEmpty
        $manifest.AliasesToExport | Should -Not -BeNullOrEmpty
    }

    It 'imports the binary-backed module successfully' {
        $module.Name | Should -Be 'PSMaintenance'
        $module.ExportedCmdlets.Keys | Should -Not -BeNullOrEmpty
    }

    It 'exports the expected cmdlets' {
        $diff = Compare-Object -ReferenceObject ($script:expectedCmdlets | Sort-Object) -DifferenceObject ($module.ExportedCmdlets.Keys | Sort-Object)
        $diff | Should -BeNullOrEmpty
    }

    It 'exports the expected aliases' {
        $diff = Compare-Object -ReferenceObject ($script:expectedAliases | Sort-Object) -DifferenceObject ($module.ExportedAliases.Keys | Sort-Object)
        $diff | Should -BeNullOrEmpty
    }

    It 'maps aliases to exported cmdlets' {
        (Get-Command Install-Documentation).ResolvedCommandName | Should -Be 'Install-ModuleDocumentation'
        (Get-Command Set-Documentation).ResolvedCommandName | Should -Be 'Set-ModuleDocumentation'
        (Get-Command Show-Documentation).ResolvedCommandName | Should -Be 'Show-ModuleDocumentation'
    }
}

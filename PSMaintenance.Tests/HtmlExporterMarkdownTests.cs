using System;
using System.IO;
using Xunit;

namespace PSMaintenance.Tests;

public class HtmlExporterMarkdownTests
{
    [Fact]
    public void Export_UsesOfficeImoMarkdownProvider_ForAlternateFences()
    {
        var exporter = new HtmlExporter();
        var module = new ModuleInfoModel
        {
            Name = "ugit",
            Version = "1.0.0",
            SkipCommands = true,
            SkipDependencies = true
        };

        var items = new[]
        {
            new DocumentItem
            {
                Title = "README",
                Kind = "FILE",
                Source = "Local",
                Content = """
                ## Getting started
                ~~~ps1
                # Install ugit from the PowerShell Gallery
                Install-Module ugit -Scope CurrentUser
                # Then import it.
                Import-Module ugit -Force -PassThru
                ~~~
                """
            }
        };

        var outputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");

        try
        {
            exporter.Export(module, items, outputPath, open: false);

            var html = File.ReadAllText(outputPath);
            Assert.DoesNotContain("<p>~~~ps1</p>", html, StringComparison.Ordinal);
            Assert.Contains("language-powershell", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Install-Module ugit -Scope CurrentUser", html, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }
}

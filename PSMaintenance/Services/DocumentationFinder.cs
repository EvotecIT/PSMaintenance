// ReSharper disable All
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Management.Automation;

namespace PSMaintenance;

/// <summary>
/// Resolves module base paths (root and Internals) and maps delivery options from the manifest.
/// Provides helpers to locate standard documents (README/CHANGELOG/LICENSE/UPGRADE).
/// </summary>
internal sealed class DocumentationFinder
{
    private readonly PSCmdlet _cmdlet;
    public DocumentationFinder(PSCmdlet cmdlet) => _cmdlet = cmdlet;

    /// <summary>
    /// Resolves module root and Internals locations along with delivery options from the manifest.
    /// </summary>
    public (string RootBase, string? InternalsBase, DeliveryOptions Options) ResolveBases(PSModuleInfo module)
    {
        var root = module.ModuleBase;
        string? internals = null;
        var opts = new DeliveryOptions();

        var manifestPath = Path.Combine(root, module.Name + ".psd1");
        try
        {
            var sb = _cmdlet.InvokeCommand.NewScriptBlock("$m = Test-ModuleManifest -Path $args[0]; $m.PrivateData.PSData.Delivery");
            var delivery = sb.Invoke(manifestPath).FirstOrDefault() as PSObject;
            if (delivery != null)
            {
                opts.InternalsPath = (string)(Get(delivery, "InternalsPath") ?? "Internals");
                var sp = Get(delivery, "ScriptsPath") as string;
                if (!string.IsNullOrWhiteSpace(sp)) opts.ScriptsPath = sp!;
                var docs = Get(delivery, "DocsPaths");
                if (docs is System.Collections.IEnumerable en)
                {
                    var list = new System.Collections.Generic.List<string>();
                    foreach (var o in en) { var s = o?.ToString(); if (!string.IsNullOrWhiteSpace(s)) list.Add(s!); }
                    opts.DocsPaths = list;
                }
                opts.IntroFile = Get(delivery, "IntroFile") as string;
                opts.UpgradeFile = Get(delivery, "UpgradeFile") as string;
            }
        }
        catch { /* ignore manifest parse failures */ }

        var internalsCandidate = Path.Combine(root, opts.InternalsPath ?? "Internals");
        if (Directory.Exists(internalsCandidate)) internals = internalsCandidate;
        return (root, internals, opts);
    }

    /// <summary>
    /// Resolves a file by <see cref="DocumentKind"/> from root or Internals depending on preference.
    /// </summary>
    public FileInfo? ResolveDocument((string RootBase, string? InternalsBase, DeliveryOptions Options) bases, DocumentKind kind, bool preferInternals)
    {
        string pattern = kind switch
        {
            DocumentKind.Readme => "README*",
            DocumentKind.Changelog => "CHANGELOG*",
            DocumentKind.License => "LICENSE*",
            DocumentKind.Upgrade => "UPGRADE*",
            _ => ""
        };

        if (string.IsNullOrEmpty(pattern)) return null;

        var root = bases.RootBase;
        var internals = bases.InternalsBase;

        FileInfo? first(DirectoryInfo d)
            => d.Exists ? d.GetFiles(pattern).OrderBy(f => f.Name.Length).FirstOrDefault() : null;

        var rootPick = first(new DirectoryInfo(root));
        var internalsPick = internals != null ? first(new DirectoryInfo(internals)) : null;

        if (preferInternals && internalsPick != null) return (FileInfo?)internalsPick;
        return (FileInfo?)(rootPick ?? internalsPick);
    }

    private static object? Get(object obj, string name)
    {
        if (obj is PSObject pso)
        {
            var prop = pso.Properties[name];
            if (prop != null) return prop.Value;
            obj = pso.BaseObject;
        }
        if (obj is IDictionary dict)
        {
            return dict.Contains(name) ? dict[name] : null;
        }
        var type = obj.GetType();
        var pi = type.GetProperty(name);
        return pi != null ? pi.GetValue(obj) : null;
    }
}

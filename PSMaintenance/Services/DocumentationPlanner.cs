using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PSMaintenance;

/// <summary>
/// Plans which documents to show/export based on local availability and repository settings.
/// </summary>
internal sealed class DocumentationPlanner
{
    private readonly DocumentationFinder _finder;
    public DocumentationPlanner(DocumentationFinder finder) => _finder = finder;

    internal sealed class Request
    {
        public string RootBase { get; set; } = string.Empty;
        public string? InternalsBase { get; set; }
        public object? Delivery { get; set; }
        public string? ProjectUri { get; set; }
        public string? RepositoryBranch { get; set; }
        public string? RepositoryToken { get; set; }
        public string[]? RepositoryPaths { get; set; }
        public bool PreferInternals { get; set; }
        public bool Readme { get; set; }
        public bool Changelog { get; set; }
        public bool License { get; set; }
        public bool Intro { get; set; }
        public bool Upgrade { get; set; }
        public bool All { get; set; }
        public bool Online { get; set; }
        public DocumentationMode Mode { get; set; } = DocumentationMode.PreferLocal;
        public bool ShowDuplicates { get; set; }
        public string? SingleFile { get; set; }
        public string? TitleName { get; set; }
        public string? TitleVersion { get; set; }
        public System.Collections.Generic.IEnumerable<string>? FormatsToProcess { get; set; }
        public System.Collections.Generic.IEnumerable<string>? TypesToProcess { get; set; }
        public System.Collections.Generic.IEnumerable<string>? DocsPaths { get; set; }
        public string? LocalChangelogPath { get; set; }
    }

    internal sealed class Result
    {
        public List<DocumentItem> Items { get; } = new List<DocumentItem>();
        public bool UsedRemote { get; set; }
    }

    public Result Execute(Request req)
    {
        return Execute(req, null);
    }

    public Result Execute(Request req, IRepoClient? clientOverride)
    {
        var res = new Result();
        var items = new List<(string Kind, string Path)>();

        // Specific file selection
        if (!string.IsNullOrEmpty(req.SingleFile))
        {
            var t1 = Path.Combine(req.RootBase, req.SingleFile);
            var t2 = req.InternalsBase != null ? Path.Combine(req.InternalsBase, req.SingleFile) : null;
            if (File.Exists(t1)) items.Add(("FILE", t1));
            else if (t2 != null && File.Exists(t2)) items.Add(("FILE", t2));
            else throw new FileNotFoundException($"File '{req.SingleFile}' not found under root or Internals.");
        }

        // Intro/Upgrade/All toggles
        if (req.Intro) items.Add(("INTRO", string.Empty));
        if (req.All)
        {
            if (!req.Intro) items.Add(("INTRO", string.Empty));
            AddKind(items, req, DocumentKind.Readme, includeEvenIfSelected: !req.Readme);
            AddKind(items, req, DocumentKind.Changelog, includeEvenIfSelected: !req.Changelog);
            AddKind(items, req, DocumentKind.License, includeEvenIfSelected: !req.License);
        }

        if (req.Readme) AddKind(items, req, DocumentKind.Readme);
        if (req.Changelog) AddKind(items, req, DocumentKind.Changelog);
        if (req.License) AddKind(items, req, DocumentKind.License);
        if (req.Upgrade) items.Add(("UPGRADE", string.Empty));

        // Default selection when nothing specified: include all known docs
        bool hasSelectors = req.Readme || req.Changelog || req.License || req.All || req.Intro || req.Upgrade || !string.IsNullOrEmpty(req.SingleFile);
        if (!hasSelectors)
        {
            AddKind(items, req, DocumentKind.Readme);
            AddKind(items, req, DocumentKind.Changelog);
            AddKind(items, req, DocumentKind.License);
            AddKind(items, req, DocumentKind.Upgrade);
            // Add intro when IntroText/IntroFile present
            var introLines = GetDeliveryValue(req.Delivery, "IntroText") as System.Collections.IEnumerable;
            var introFile = GetDeliveryValue(req.Delivery, "IntroFile") as string;
            if (introLines != null || !string.IsNullOrEmpty(introFile)) items.Add(("INTRO", string.Empty));
        }

        // Remote repo fetch only when Online is requested
        var wantRemote = req.Online;
        if (wantRemote && (!string.IsNullOrWhiteSpace(req.ProjectUri) || clientOverride != null))
        {
            var client = clientOverride;
            if (client == null)
            {
                var info = RepoUrlParser.Parse(req.ProjectUri!);
                var token = ResolveToken(req.RepositoryToken);
                if (string.IsNullOrEmpty(token))
                {
                    // Try persisted token based on host (GitHub/Azure DevOps)
                    token = TokenStore.GetToken(info.Host) ?? string.Empty;
                }
                client = RepoClientFactory.Create(info, token);
            }
            if (client != null)
            {
                string branch = string.IsNullOrWhiteSpace(req.RepositoryBranch) ? client.GetDefaultBranch() : req.RepositoryBranch!;
                // Default remote files (always add candidates; selection policy is applied later in the exporter)
                var readme = TryFetchFirst(client, branch, new[] { "README.md", "README.MD", "Readme.md" });
                var changelog = TryFetchFirst(client, branch, new[] { "CHANGELOG.md", "CHANGELOG.MD", "Changelog.md" });
                var license = TryFetchFirst(client, branch, new[] { "LICENSE", "LICENSE.md", "LICENSE.txt" });

                bool anyRemote = false;
                if (!string.IsNullOrEmpty(readme))
                {
                    var baseUri = BuildRawBase(req.ProjectUri, branch);
                    var di = MakeContentItem(req, "README", RewriteRelativeLinks(readme!, baseUri));
                    di.Source = "Remote"; di.FileName = "README.md"; di.Title = "README"; di.BaseUri = baseUri;
                    res.Items.Add(di); anyRemote = true;
                }
                if (!string.IsNullOrEmpty(changelog))
                {
                    var baseUri = BuildRawBase(req.ProjectUri, branch);
                    var di = MakeContentItem(req, "CHANGELOG", RewriteRelativeLinks(changelog!, baseUri));
                    di.Source = "Remote"; di.FileName = "CHANGELOG.md"; di.Title = "CHANGELOG"; di.BaseUri = baseUri;
                    res.Items.Add(di); anyRemote = true;
                }
                if (!string.IsNullOrEmpty(license))
                {
                    var di = MakeContentItem(req, "LICENSE", license!);
                    di.Source = "Remote"; di.FileName = "LICENSE"; di.Title = "LICENSE";
                    res.Items.Add(di); anyRemote = true;
                }
                // Extra paths
                if (req.RepositoryPaths != null)
                {
                    foreach (var rp in req.RepositoryPaths)
                    {
                            foreach (var (Name, Path) in client.ListFiles(rp, branch))
                            {
                                var lowerName = (Name ?? string.Empty).ToLowerInvariant();
                                var ext = System.IO.Path.GetExtension(Name)?.ToLowerInvariant();
                                var content = client.GetFileContent(Path, branch);
                                if (string.IsNullOrEmpty(content)) continue;

                                if (lowerName.StartsWith("about_") && lowerName.EndsWith(".help.txt"))
                                {
                                    res.Items.Add(new DocumentItem
                                    {
                                        Title = Name ?? string.Empty,
                                        Kind = "ABOUT",
                                        Content = AboutToMarkdown(content!),
                                        FileName = Name,
                                        Path = Path,
                                        Source = "Remote",
                                        BaseUri = BuildRawBase(req.ProjectUri, branch)
                                    });
                                    anyRemote = true;
                                    continue;
                                }

                                if (IsCommunityFile(lowerName))
                                {
                                    res.Items.Add(new DocumentItem
                                    {
                                        Title = Name ?? string.Empty,
                                        Kind = "COMMUNITY",
                                        Content = RewriteRelativeLinks(content!, BuildRawBase(req.ProjectUri, branch)),
                                        FileName = Name,
                                        Path = Path,
                                        Source = "Remote",
                                        BaseUri = BuildRawBase(req.ProjectUri, branch)
                                    });
                                    anyRemote = true;
                                    continue;
                                }

                            if (ext == ".md" || ext == ".markdown" || ext == ".txt" || ext == ".help" || ext == ".help.txt")
                            {
                                // Treat repository path content as documentation pages, not standard tabs
                                res.Items.Add(new DocumentItem
                                {
                                    Title = Name ?? string.Empty,
                                    Kind = "DOC",
                                    Content = RewriteRelativeLinks(content!, BuildRawBase(req.ProjectUri, branch)),
                                    FileName = Name,
                                    Path = Path,
                                    Source = "Remote",
                                    BaseUri = BuildRawBase(req.ProjectUri, branch)
                                });
                                anyRemote = true;
                            }
                        }
                    }
                }
                res.UsedRemote = anyRemote;
                // Remote items are added first; local will be added below when resolving 'items'.
            }
        }

        // Resolve local items into DocumentItem list
        foreach (var it in items)
        {
            if (it.Kind == "FILE")
            {
                var fileName = System.IO.Path.GetFileName(it.Path);
                var title = BuildTitle(req, fileName);
                res.Items.Add(new DocumentItem { Title = title, Kind = "FILE", Path = it.Path, FileName = fileName, Source = "Local" });
                continue;
            }
            if (it.Kind == "INTRO")
            {
                var introLines = GetDeliveryValue(req.Delivery, "IntroText") as System.Collections.IEnumerable;
                var introFile = GetDeliveryValue(req.Delivery, "IntroFile") as string;
                string content = string.Empty;
                if (introLines != null)
                {
                    content = string.Join("\n", introLines.Cast<object>().Select(o => o?.ToString() ?? string.Empty));
                }
                else if (!string.IsNullOrEmpty(introFile))
                {
                    var p1 = Path.Combine(req.RootBase, introFile);
                    if (File.Exists(p1)) content = File.ReadAllText(p1);
                }
                if (!string.IsNullOrWhiteSpace(content))
                    res.Items.Add(new DocumentItem { Title = BuildTitle(req, "Introduction"), Kind = "FILE", Content = content });
                continue;
            }
            if (it.Kind == "UPGRADE")
            {
                // Prefer UpgradeText from delivery, else resolve file
                var upLines = GetDeliveryValue(req.Delivery, "UpgradeText") as System.Collections.IEnumerable;
                if (upLines != null)
                {
                    var content = string.Join("\n", upLines.Cast<object>().Select(o => o?.ToString() ?? string.Empty));
                    res.Items.Add(new DocumentItem { Title = BuildTitle(req, "Upgrade"), Kind = "FILE", Content = content });
                }
                else
                {
                    var f = _Resolve(req, DocumentKind.Upgrade);
                    if (f != null)
                    {
                        res.Items.Add(new DocumentItem { Title = BuildTitle(req, f.Name), Kind = "FILE", Path = f.FullName });
                    }
                }
            }
        }

        // About topics (local) – always included by default
        try
        {
            var aboutFiles = _finder.ResolveAboutTopics((req.RootBase, req.InternalsBase, new DeliveryOptions()), req.DocsPaths);
            foreach (var f in aboutFiles)
            {
                var title = BuildTitle(req, StripAboutExtensions(f.Name));
                string raw;
                try { raw = System.IO.File.ReadAllText(f.FullName); }
                catch { continue; }
                res.Items.Add(new DocumentItem { Title = title, Kind = "ABOUT", Path = f.FullName, FileName = f.Name, Content = AboutToMarkdown(raw), Source = "Local" });
            }
        }
        catch { }

        // Formats and Types (local)
        try
        {
            var formats = _finder.ResolveFormatFiles((req.RootBase, req.InternalsBase, new DeliveryOptions()), req.FormatsToProcess);
            foreach (var f in formats)
            {
                var content = System.IO.File.ReadAllText(f.FullName);
                res.Items.Add(new DocumentItem { Title = BuildTitle(req, f.Name), Kind = "FORMAT", Path = f.FullName, FileName = f.Name, Content = "```xml\n" + content + "\n```", Source = "Local" });
            }

            var types = _finder.ResolveTypesFiles((req.RootBase, req.InternalsBase, new DeliveryOptions()), req.TypesToProcess);
            foreach (var f in types)
            {
                var content = System.IO.File.ReadAllText(f.FullName);
                res.Items.Add(new DocumentItem { Title = BuildTitle(req, f.Name), Kind = "TYPE", Path = f.FullName, FileName = f.Name, Content = "```xml\n" + content + "\n```", Source = "Local" });
            }
        }
        catch { }

        // Community files (local)
        try
        {
            var community = _finder.ResolveCommunityFiles((req.RootBase, req.InternalsBase, new DeliveryOptions()), req.DocsPaths);
            foreach (var f in community)
            {
                string content; try { content = File.ReadAllText(f.FullName); } catch { continue; }
                res.Items.Add(new DocumentItem { Title = BuildTitle(req, f.Name), Kind = "COMMUNITY", Path = f.FullName, FileName = f.Name, Content = content, Source = "Local" });
            }
        }
        catch { }

        // Remote standard docs (README/CHANGELOG/LICENSE)
        try
        {
            bool wantRemoteFetch = !string.IsNullOrWhiteSpace(req.ProjectUri);
            if (wantRemoteFetch)
            {
                bool hasReadme = res.Items.Any(i => i.Kind == "FILE" && ((i.FileName ?? i.Title).StartsWith("README", StringComparison.OrdinalIgnoreCase)));
                bool hasChlog  = res.Items.Any(i => i.Kind == "FILE" && ((i.FileName ?? i.Title).StartsWith("CHANGELOG", StringComparison.OrdinalIgnoreCase)));
                bool hasLic    = res.Items.Any(i => i.Kind == "FILE" && ((i.FileName ?? i.Title).StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase)));
                bool forceRemoteStandard = (req.Mode == DocumentationMode.All || req.Mode == DocumentationMode.PreferRemote);

                var info = RepoUrlParser.Parse(req.ProjectUri!);
                var token = ResolveToken(req.RepositoryToken);
                if (string.IsNullOrEmpty(token)) { token = TokenStore.GetToken(info.Host) ?? string.Empty; }
                var client = RepoClientFactory.Create(info, token);
                if (client != null)
                {
                    string branch = string.IsNullOrWhiteSpace(req.RepositoryBranch) ? client.GetDefaultBranch() : req.RepositoryBranch!;
                    if (forceRemoteStandard || !hasReadme)
                    {
                        var readme = TryFetchFirst(client, branch, new[] { "README.md", "README.MD", "Readme.md" });
                        if (!string.IsNullOrEmpty(readme)) { var di = MakeContentItem(req, "README", RewriteRelativeLinks(readme!, BuildRawBase(req.ProjectUri, branch))); di.Source = "Remote"; di.FileName = "README.md"; di.Title = "README"; res.Items.Add(di); }
                    }
                    if (forceRemoteStandard || !hasChlog)
                    {
                        var ch = TryFetchFirst(client, branch, new[] { "CHANGELOG.md", "CHANGELOG.MD", "Changelog.md" });
                        if (!string.IsNullOrEmpty(ch)) { var di = MakeContentItem(req, "CHANGELOG", RewriteRelativeLinks(ch!, BuildRawBase(req.ProjectUri, branch))); di.Source = "Remote"; di.FileName = "CHANGELOG.md"; di.Title = "CHANGELOG"; res.Items.Add(di); }
                    }
                    if (forceRemoteStandard || !hasLic)
                    {
                        var lc = TryFetchFirst(client, branch, new[] { "LICENSE", "LICENSE.md", "LICENSE.txt" });
                        if (!string.IsNullOrEmpty(lc)) { var di = MakeContentItem(req, "LICENSE", lc!); di.Source = "Remote"; di.FileName = "LICENSE"; di.Title = "LICENSE"; res.Items.Add(di); }
                    }
                }

                // Include repository Docs/ folder similar to Internals\Docs
                try
                {
                    var info2 = RepoUrlParser.Parse(req.ProjectUri!);
                    var token2 = ResolveToken(req.RepositoryToken);
                    if (string.IsNullOrEmpty(token2))
                    {
                        token2 = TokenStore.GetToken(info2.Host) ?? string.Empty;
                    }
                    var client2 = RepoClientFactory.Create(info2, token2);
                    if (client2 != null)
                    {
                        string branch2 = string.IsNullOrWhiteSpace(req.RepositoryBranch) ? client2.GetDefaultBranch() : req.RepositoryBranch!;
                        var roots = (req.RepositoryPaths != null && req.RepositoryPaths.Length > 0)
                            ? req.RepositoryPaths
                            : new [] { "docs", "Docs" };

                        var collected = new List<(string Name, string Path)>();
                        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
                        {
                            foreach (var item in client2.ListFiles(root, branch2))
                            {
                                var n = item.Name ?? string.Empty;
                                if (n.StartsWith("about_", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".help.txt", StringComparison.OrdinalIgnoreCase))
                                {
                                    collected.Add(item);
                                    continue;
                                }
                                if (n.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".help", StringComparison.OrdinalIgnoreCase))
                                {
                                    collected.Add(item);
                                }
                            }
                        }
                        if (collected.Count > 0)
                        {
                            // Apply DocumentationOrder if provided
                            var orderArr = GetDeliveryValue(req.Delivery, "DocumentationOrder") as System.Collections.IEnumerable;
                            var order = new List<string>();
                            if (orderArr != null)
                            {
                                foreach (var o in orderArr) { var s = o?.ToString(); if (!string.IsNullOrWhiteSpace(s)) order.Add(s!); }
                            }
                            IEnumerable<(string Name,string Path)> ordered;
                            if (order.Count > 0)
                            {
                                var map = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
                                for (int i=0;i<order.Count;i++) map[order[i]] = i;
                                ordered = collected.OrderBy(f => map.ContainsKey(f.Name) ? map[f.Name] : int.MaxValue)
                                                   .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase);
                            }
                            else
                            {
                                ordered = collected.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase);
                            }

                            foreach (var f in ordered)
                            {
                                var content = client2.GetFileContent(f.Path, branch2);
                                if (string.IsNullOrEmpty(content)) continue;
                                if (f.Name.StartsWith("about_", StringComparison.OrdinalIgnoreCase) && f.Name.EndsWith(".help.txt", StringComparison.OrdinalIgnoreCase))
                                {
                                    res.Items.Add(new DocumentItem { Title = f.Name, Kind = "ABOUT", Content = AboutToMarkdown(content!), FileName = f.Name, Path = f.Path, Source = "Remote", BaseUri = BuildRawBase(req.ProjectUri, branch2) });
                                    continue;
                                }
                                if (IsCommunityFile(f.Name))
                                {
                                    res.Items.Add(new DocumentItem { Title = f.Name, Kind = "COMMUNITY", Content = RewriteRelativeLinks(content!, BuildRawBase(req.ProjectUri, branch2)), FileName = f.Name, Path = f.Path, Source = "Remote", BaseUri = BuildRawBase(req.ProjectUri, branch2) });
                                    continue;
                                }
                                res.Items.Add(new DocumentItem { Title = f.Name, Kind = "DOC", Content = RewriteRelativeLinks(content!, BuildRawBase(req.ProjectUri, branch2)), FileName = f.Name, Path = f.Path, Source = "Remote", BaseUri = BuildRawBase(req.ProjectUri, branch2) });
                            }
                        }
                    }
                }
                catch { /* ignore repo Docs failures */ }
            }
        }
        catch { /* ignore remote backfill errors */ }

        // Extra: scripts tab (Internals/Scripts and standalone ps1 under Internals)
        try
        {
            if (!string.IsNullOrEmpty(req.InternalsBase) && Directory.Exists(req.InternalsBase))
            {
                var scriptRoots = new [] {
                    Path.Combine(req.InternalsBase!, "Scripts"),
                    req.InternalsBase!
                };
                foreach (var root in scriptRoots.Distinct())
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (var f in Directory.GetFiles(root, "*.ps1", SearchOption.TopDirectoryOnly))
                    {
                        var name = Path.GetFileName(f);
                        string code;
                        try { code = File.ReadAllText(f); } catch { continue; }
                        // wrap as fenced code for HTML renderer
                        var md = $"```powershell\n{code}\n```";
                        res.Items.Add(new DocumentItem { Title = name, Kind = "SCRIPT", Content = md, FileName = name, Path = f });
                    }
                }
            }
        }
        catch { /* ignore scripts discovery errors */ }

        // Extra: docs tab (Internals/Docs/*.md)
        try
        {
            if (!string.IsNullOrEmpty(req.InternalsBase))
            {
                var docsRoot = Path.Combine(req.InternalsBase!, "Docs");
                if (Directory.Exists(docsRoot))
                {
                    var mdFiles = Directory.GetFiles(docsRoot, "*.md", SearchOption.TopDirectoryOnly)
                                            .Concat(Directory.GetFiles(docsRoot, "*.markdown", SearchOption.TopDirectoryOnly))
                                            .ToList();
                    // Optional ordering from delivery metadata
                    var orderArr = GetDeliveryValue(req.Delivery, "DocumentationOrder") as System.Collections.IEnumerable;
                    var order = new List<string>();
                    if (orderArr != null)
                    {
                        foreach (var o in orderArr) { var s = o?.ToString(); if (!string.IsNullOrWhiteSpace(s)) order.Add(s!); }
                    }
                    IEnumerable<string> ordered;
                    if (order.Count > 0)
                    {
                        // First explicit order by file name (case-insensitive), then remaining alphabetically
                        var map = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
                        for (int i=0;i<order.Count;i++) map[order[i]] = i;
                        ordered = mdFiles.OrderBy(f => map.ContainsKey(Path.GetFileName(f)) ? map[Path.GetFileName(f)] : int.MaxValue)
                                         .ThenBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);
                    }
                    else
                    {
                        ordered = mdFiles.OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);
                    }
                    foreach (var f in ordered)
                    {
                        string content; try { content = File.ReadAllText(f); } catch { continue; }
                        res.Items.Add(new DocumentItem { Title = Path.GetFileName(f), Kind = "DOC", Content = content, FileName = Path.GetFileName(f), Path = f, Source = "Local" });
                    }
                }
            }
        }
        catch { /* ignore docs discovery errors */ }

        // Links
        var links = GetDeliveryValue(req.Delivery, "ImportantLinks") as System.Collections.IEnumerable;
        if (links != null)
        {
            var md = new System.Text.StringBuilder();
            md.AppendLine("# Links");
            foreach (var l in links)
            {
                var t = GetDeliveryValue(l, "Title")?.ToString() ?? GetDeliveryValue(l, "Name")?.ToString();
                var u = GetDeliveryValue(l, "Url")?.ToString();
                if (string.IsNullOrEmpty(u)) continue;
                if (!string.IsNullOrEmpty(t)) md.Append("- [").Append(t).Append("](").Append(u).AppendLine(")");
                else md.Append("- ").AppendLine(u);
            }
            if (md.Length > 0)
                res.Items.Add(new DocumentItem { Title = BuildTitle(req, "Links"), Kind = "FILE", Content = md.ToString() });
        }

        // Release summary derived from CHANGELOG
        try
        {
            string? changelogContent = null;
            var remoteChangelog = res.Items.FirstOrDefault(i => string.Equals(i.Kind, "FILE", StringComparison.OrdinalIgnoreCase) && string.Equals(i.FileName, "CHANGELOG.md", StringComparison.OrdinalIgnoreCase) && string.Equals(i.Source, "Remote", StringComparison.OrdinalIgnoreCase));
            if (remoteChangelog != null) changelogContent = remoteChangelog.Content;
            if (string.IsNullOrEmpty(changelogContent))
            {
                if (!string.IsNullOrEmpty(req.LocalChangelogPath) && File.Exists(req.LocalChangelogPath))
                {
                    changelogContent = File.ReadAllText(req.LocalChangelogPath);
                }
            }
            if (string.IsNullOrEmpty(changelogContent))
            {
                var localChlog = res.Items.FirstOrDefault(i => string.Equals(i.Kind, "FILE", StringComparison.OrdinalIgnoreCase) && (i.FileName ?? i.Title)?.StartsWith("CHANGELOG", StringComparison.OrdinalIgnoreCase) == true && string.Equals(i.Source, "Local", StringComparison.OrdinalIgnoreCase));
                if (localChlog != null)
                {
                    changelogContent = string.IsNullOrEmpty(localChlog.Content) && !string.IsNullOrEmpty(localChlog.Path) && File.Exists(localChlog.Path)
                        ? File.ReadAllText(localChlog.Path!)
                        : localChlog.Content;
                }
            }
            if (!string.IsNullOrEmpty(changelogContent))
            {
                var releasesMd = BuildReleaseSummary(changelogContent!);
                if (!string.IsNullOrWhiteSpace(releasesMd))
                {
                    res.Items.Add(new DocumentItem { Title = BuildTitle(req, "Releases"), Kind = "RELEASES", Content = releasesMd, Source = string.IsNullOrEmpty(req.ProjectUri) ? "Local" : "Derived" });
                }
            }
            // If changelog not present, try repo releases API
            if (res.Items.All(i => !string.Equals(i.Kind, "RELEASES", StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrWhiteSpace(req.ProjectUri))
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(req.ProjectUri)) {
                        var info = RepoUrlParser.Parse(req.ProjectUri!);
                        var token = ResolveToken(req.RepositoryToken);
                        if (string.IsNullOrEmpty(token)) token = TokenStore.GetToken(info.Host) ?? string.Empty;
                        var client = RepoClientFactory.Create(info, token);
                        var rels = client?.ListReleases() ?? new List<RepoRelease>();
                    if (rels.Count > 0)
                    {
                        var sb = new System.Text.StringBuilder();
                            sb.AppendLine("# Releases (repository API)");
                            foreach (var r in rels)
                            {
                                sb.Append("## ").Append(string.IsNullOrEmpty(r.Name) ? r.Tag : r.Name);
                                if (r.PublishedAt.HasValue) sb.Append(" (" + r.PublishedAt.Value.ToString("yyyy-MM-dd") + ")");
                                sb.AppendLine();
                                if (!string.IsNullOrWhiteSpace(r.Body))
                                {
                                    var baseUri = BuildRawBase(req.ProjectUri, r.Tag);
                                    sb.AppendLine(RewriteRelativeLinks(r.Body.Trim(), baseUri)).AppendLine();
                                }
                                if (r.Assets.Count > 0)
                                {
                                    sb.AppendLine("### Assets");
                                    foreach (var a in r.Assets)
                                    {
                                        sb.Append("- [").Append(a.Name).Append("](").Append(a.DownloadUrl).Append(")");
                                        if (a.Size.HasValue) sb.Append($" ({a.Size.Value / 1024} KB)");
                                        if (!string.IsNullOrEmpty(a.ContentType)) sb.Append($" {a.ContentType}");
                                        sb.AppendLine();
                                    }
                                    sb.AppendLine();
                                }
                            }
                            res.Items.Add(new DocumentItem { Title = BuildTitle(req, "Releases"), Kind = "RELEASES", Content = sb.ToString(), Source = "Remote", BaseUri = BuildRawBase(req.ProjectUri, rels.FirstOrDefault()?.Tag) });
                        }
                    }
                }
                catch { }
            }
        }
        catch { }

        return res;
    }

    private void AddKind(List<(string Kind,string Path)> items, Request req, DocumentKind kind, bool includeEvenIfSelected = true)
    {
        var f = _Resolve(req, kind);
        if (f != null) items.Add(("FILE", f.FullName));
    }

    private FileInfo? _Resolve(Request req, DocumentKind kind)
        => _finder.ResolveDocument((req.RootBase, req.InternalsBase, new DeliveryOptions()), kind, req.PreferInternals);

    private static string ResolveToken(string? explicitToken)
        => explicitToken
           ?? Environment.GetEnvironmentVariable("PG_GITHUB_TOKEN")
           ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN")
           ?? Environment.GetEnvironmentVariable("PG_AZDO_PAT")
           ?? Environment.GetEnvironmentVariable("AZURE_DEVOPS_EXT_PAT")
           ?? string.Empty;

    private static string? TryFetchFirst(IRepoClient client, string branch, string[] candidates)
    {
        foreach (var p in candidates)
        {
            var s = client.GetFileContent(p, branch);
            if (!string.IsNullOrEmpty(s)) return s;
        }
        return null;
    }

    private static DocumentItem MakeContentItem(Request req, string name, string content)
        => new DocumentItem { Title = BuildTitle(req, name), Kind = "FILE", Content = content };

    private static string BuildTitle(Request req, string leaf)
        => !string.IsNullOrEmpty(req.TitleName) ? $"{req.TitleName} {req.TitleVersion} - {leaf}" : leaf;
    private static string StripAboutExtensions(string name)
    {
        var n = name;
        if (n.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
        if (n.EndsWith(".help", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 5);
        return n;
    }

    private static bool IsCommunityFile(string lowerName)
    {
        if (string.IsNullOrEmpty(lowerName)) return false;
        var trimmed = lowerName.Replace('-', '_');
        return trimmed.Contains("contributing")
               || trimmed.Contains("security")
               || trimmed.Contains("support")
               || trimmed.Contains("code_of_conduct")
               || trimmed.Contains("code_of_codunduct");
    }

    private static string AboutToMarkdown(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return string.Empty;
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var sb = new System.Text.StringBuilder(content.Length + 256);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) { sb.AppendLine(); continue; }
            // Uppercase headings become H3 for readability
            bool looksHeading = trimmed.Length <= 40 && trimmed.ToUpperInvariant() == trimmed && trimmed.All(ch => !char.IsLetter(ch) || char.IsUpper(ch));
            if (looksHeading)
            {
                var title = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(trimmed.ToLowerInvariant());
                sb.Append("### ").Append(title).AppendLine();
            }
            else
            {
                sb.AppendLine(trimmed);
            }
        }
        return sb.ToString();
    }

    private static string BuildReleaseSummary(string changelogContent)
    {
        if (string.IsNullOrWhiteSpace(changelogContent)) return string.Empty;
        var lines = changelogContent.Replace("\r\n", "\n").Split('\n');
        var releases = new System.Collections.Generic.List<(string Version,string Title)>();
        var rx = new System.Text.RegularExpressions.Regex("^##\\s*\\[?v?(?<ver>[^\\]]+)\\]?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (var line in lines)
        {
            var m = rx.Match(line.Trim());
            if (m.Success)
            {
                var ver = m.Groups["ver"].Value.Trim();
                releases.Add((ver, line.TrimStart('#',' ')));
            }
        }
        if (releases.Count == 0) return string.Empty;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Releases (from CHANGELOG)");
        foreach (var r in releases)
        {
            sb.Append("- ").Append(r.Version).AppendLine();
        }
        return sb.ToString();
    }

    private static string? BuildRawBase(string? projectUri, string? refName)
    {
        var normalizedProjectUri = projectUri?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedProjectUri)) return null;
        try
        {
            var info = RepoUrlParser.Parse(normalizedProjectUri!);
            if (info.Host == RepoHost.GitHub && !string.IsNullOrEmpty(info.Owner) && !string.IsNullOrEmpty(info.Repo))
            {
                var branch = "main";
                if (!string.IsNullOrWhiteSpace(refName))
                {
                    branch = refName!.Trim();
                }
                return $"https://raw.githubusercontent.com/{info.Owner}/{info.Repo}/{branch}/";
            }
        }
        catch { }
        return null;
    }

    private static string RewriteRelativeLinks(string markdown, string? baseUri)
    {
        if (string.IsNullOrWhiteSpace(markdown) || string.IsNullOrWhiteSpace(baseUri)) return markdown ?? string.Empty;
        string repl(Match m)
        {
            var url = m.Groups[2].Value;
            if (string.IsNullOrWhiteSpace(url)) return m.Value;
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return m.Value;
            if (url.StartsWith("//") || url.StartsWith("#") || url.StartsWith("mailto:") || url.StartsWith("data:")) return m.Value;
            try
            {
                var abs = new Uri(new Uri(baseUri!, UriKind.Absolute), url).ToString();
                return m.Groups[1].Value + abs + m.Groups[3].Value;
            }
            catch { return m.Value; }
        }

        // Matches [text](url) and ![alt](url)
        var pattern = @"(!?\[[^\]]*\]\()([^\)]+)(\))";
        return Regex.Replace(markdown, pattern, new MatchEvaluator(repl));
    }

    private static object? GetDeliveryValue(object? delivery, string name)
    {
        if (delivery == null || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (delivery is System.Collections.IDictionary dictionary)
        {
            return dictionary.Contains(name) ? dictionary[name] : null;
        }

        var type = delivery.GetType();
        var directProperty = type.GetProperty(name);
        if (directProperty != null)
        {
            return directProperty.GetValue(delivery);
        }

        var propertiesProperty = type.GetProperty("Properties");
        var properties = propertiesProperty?.GetValue(delivery);
        if (properties != null)
        {
            var indexer = properties.GetType().GetProperty("Item", new[] { typeof(string) });
            var property = indexer?.GetValue(properties, new object[] { name });
            if (property != null)
            {
                return property.GetType().GetProperty("Value")?.GetValue(property);
            }
        }

        return null;
    }
}

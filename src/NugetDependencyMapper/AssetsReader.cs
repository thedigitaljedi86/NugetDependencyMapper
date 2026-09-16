using System.Text.Json;

namespace NugetDependencyMapper;

public static class AssetsReader
{
    public static void Read(ProjectInfo project, string assetsPath, List<Diagnostic> diagnostics, LicenseReader? licenseReader = null)
    {
        licenseReader ??= new LicenseReader();
        using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var root = document.RootElement;
        var metadata = root.GetProperty("project");
        if (metadata.TryGetProperty("restore", out var restore) && restore.TryGetProperty("projectPath", out var projectPath))
        {
            var owner = projectPath.GetString();
            if (!string.IsNullOrEmpty(owner) && !Path.GetFullPath(owner).Equals(Path.GetFullPath(project.Path),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException("The assets file belongs to a different project.");
        }
        var frameworks = metadata.GetProperty("frameworks");
        foreach (var target in root.GetProperty("targets").EnumerateObject())
        {
            var graph = new TargetGraph { Name = target.Name };
            var tfm = target.Name.Split('/')[0];
            var direct = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var framework = frameworks.EnumerateObject().FirstOrDefault(f => f.Name == tfm ||
                (f.Value.TryGetProperty("targetAlias", out var alias) && alias.GetString() == tfm));
            if (framework.Value.ValueKind == JsonValueKind.Object && framework.Value.TryGetProperty("dependencies", out var dependencies))
                foreach (var dependency in dependencies.EnumerateObject())
                    direct[dependency.Name] = dependency.Value.ValueKind == JsonValueKind.String ? dependency.Value.GetString()! :
                        dependency.Value.TryGetProperty("version", out var version) ? version.GetString() ?? "" : "";
            if (framework.Value.ValueKind != JsonValueKind.Object)
            {
                // Older assets use long framework names in targets/dependency groups.
                if (root.TryGetProperty("projectFileDependencyGroups", out var groups) && groups.TryGetProperty(tfm, out var group))
                {
                    foreach (var item in group.EnumerateArray())
                    {
                        var declaration = item.GetString() ?? "";
                        var separator = declaration.IndexOf(' ');
                        if (separator > 0) direct[declaration[..separator]] = declaration[(separator + 1)..];
                    }
                }
                else diagnostics.Add(new("TARGET_METADATA", $"Cannot match direct dependency metadata for {target.Name}. Package versions and edges are available, but direct classification may be incomplete.", project.Id));
            }
            var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // Keys we kept, for O(1) membership tests while walking the same libraries a second time.
            var kept = new HashSet<string>(StringComparer.Ordinal);
            foreach (var library in target.Value.EnumerateObject())
            {
                var slash = library.Name.LastIndexOf('/');
                if (slash < 1) continue;
                var id = library.Name[..slash];
                var version = library.Name[(slash + 1)..];
                var type = library.Value.TryGetProperty("type", out var value) ? value.GetString() : null;
                if (type is not ("package" or "project")) continue;
                entries[id] = library.Name;
                kept.Add(library.Name);
                if (type == "project") graph.ProjectLibraries[library.Name] = id;
                else graph.Packages.Add(new(library.Name, id, version, direct.ContainsKey(id), direct.GetValueOrDefault(id), true, licenseReader.Read(root, library.Name, id)));
            }
            // Framework-provided and excluded dependencies are legitimately absent from the graph and
            // can number in the hundreds, so report them once per target instead of once per edge.
            var unresolved = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var library in target.Value.EnumerateObject())
            {
                if (!kept.Contains(library.Name) || !library.Value.TryGetProperty("dependencies", out var childDependencies)) continue;
                foreach (var dependency in childDependencies.EnumerateObject())
                {
                    if (entries.TryGetValue(dependency.Name, out var key)) graph.Edges.Add(new(library.Name, key, dependency.Value.ToString()));
                    else unresolved.Add(dependency.Name);
                }
            }
            if (unresolved.Count > 0)
                diagnostics.Add(new("UNRESOLVED_EDGE",
                    $"{target.Name}: {unresolved.Count} dependenc{(unresolved.Count == 1 ? "y is" : "ies are")} absent from the restored graph " +
                    $"({Summarize(unresolved)}). These are usually provided by the shared framework or excluded through PrivateAssets/ExcludeAssets.",
                    project.Id));
            graph.Roots.AddRange(graph.Packages.Where(p => p.Direct).Select(p => p.Key));
            // Referenced projects are additional entry points for inherited package chains.
            var referencedByProject = new HashSet<string>(graph.Edges.Where(e => graph.ProjectLibraries.ContainsKey(e.From)).Select(e => e.To), StringComparer.Ordinal);
            graph.Roots.AddRange(graph.ProjectLibraries.Keys.Where(key => !referencedByProject.Contains(key)));
            project.Targets.Add(graph);
        }
        if (project.Targets.Count == 0) throw new InvalidDataException("The assets file has no restored targets.");
        project.Resolved = true;
        if (root.TryGetProperty("logs", out var logs))
            foreach (var log in logs.EnumerateArray())
                diagnostics.Add(new(log.TryGetProperty("code", out var code) ? code.ToString() : "NUGET",
                    log.TryGetProperty("message", out var message) ? message.ToString() : log.ToString(), project.Id,
                    // NuGet's own warnings (NU1603, NU1701, ...) are common in healthy repositories.
                    log.TryGetProperty("level", out var level) && string.Equals(level.GetString(), "Error", StringComparison.OrdinalIgnoreCase)
                        ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning));
    }

    private static string Summarize(IEnumerable<string> names)
    {
        var listed = names.Take(5).ToList();
        return string.Join(", ", listed) + (names.Count() > listed.Count ? ", …" : "");
    }
}

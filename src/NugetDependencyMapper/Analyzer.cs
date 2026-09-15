using System.Text.Json;
using System.Xml.Linq;

namespace NugetDependencyMapper;

public static class Analyzer
{
    public static DependencyReport Analyze(string input, string? assetsRoot = null, Action<string, int, int>? onProgress = null)
    {
        var files = Discovery.Find(input);
        if (files.Count == 0) throw new InvalidOperationException("No .NET projects were found in the selected input.");
        var root = Directory.Exists(input) ? Path.GetFullPath(input) : Path.GetDirectoryName(Path.GetFullPath(input))!;
        var report = new DependencyReport { Name = Directory.Exists(input) ? new DirectoryInfo(root).Name : Path.GetFileName(input) };
        var customAssets = IndexAssets(assetsRoot);
        var licenses = new LicenseReader();
        var scanned = 0;
        foreach (var file in files)
        {
            onProgress?.Invoke(file, ++scanned, files.Count);
            var project = new ProjectInfo { Id = Path.GetRelativePath(root, file).Replace('\\', '/'), Name = Path.GetFileNameWithoutExtension(file), Path = file };
            report.Projects.Add(project);
            if (!File.Exists(file))
            {
                report.Diagnostics.Add(new("MISSING_PROJECT", "The referenced project file does not exist.", project.Id));
                continue;
            }
            var assets = customAssets.GetValueOrDefault(file) ?? Path.Combine(Path.GetDirectoryName(file)!, "obj", "project.assets.json");
            if (File.Exists(assets))
            {
                try
                {
                    AssetsReader.Read(project, assets, report.Diagnostics, licenses);
                    if (InputsNewerThan(file, File.GetLastWriteTimeUtc(assets)))
                        report.Diagnostics.Add(new("STALE_RESTORE", "Project or directory build/package settings are newer than the assets file. Run with --restore to refresh dependencies.", project.Id));
                    continue;
                }
                catch (Exception ex) when (ex is JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException or IOException)
                {
                    project.Targets.Clear();
                    project.Resolved = false;
                    report.Diagnostics.Add(new("INVALID_ASSETS", $"Cannot read restored dependencies: {ex.Message}", project.Id));
                }
            }
            ReadDeclared(project, report.Diagnostics);
        }
        ReportIndex.Build(report);
        return report;
    }

    private static Dictionary<string, string> IndexAssets(string? directory)
    {
        var index = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (directory is null) return index;
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"Assets directory does not exist: {directory}");
        foreach (var file in Directory.EnumerateFiles(directory, "project.assets.json", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var path = document.RootElement.GetProperty("project").GetProperty("restore").GetProperty("projectPath").GetString();
            if (path is null) continue;
            path = Path.GetFullPath(path);
            if (!index.TryAdd(path, file)) throw new InvalidDataException($"Multiple assets files found for {path}. Select a narrower --assets-root.");
        }
        return index;
    }

    private static bool InputsNewerThan(string file, DateTime restored)
    {
        if (File.GetLastWriteTimeUtc(file) > restored) return true;
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(file)!); dir is not null; dir = dir.Parent)
            foreach (var name in new[] { "Directory.Packages.props", "Directory.Build.props", "Directory.Build.targets", "NuGet.Config", "nuget.config", "global.json" })
                if (File.Exists(Path.Combine(dir.FullName, name)) && File.GetLastWriteTimeUtc(Path.Combine(dir.FullName, name)) > restored) return true;
        return false;
    }

    private static void ReadDeclared(ProjectInfo project, List<Diagnostic> diagnostics)
    {
        diagnostics.Add(new("DECLARED_ONLY", "No usable restore graph. Showing unevaluated declarations only; conditions, imports, transitive dependencies and installed versions are unknown. Run dotnet restore or use --restore. For custom intermediate paths, use --assets-root.", project.Id));
        try
        {
            var document = XDocument.Load(project.Path);
            var central = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var dir = new DirectoryInfo(Path.GetDirectoryName(project.Path)!); dir is not null; dir = dir.Parent)
            {
                var props = Path.Combine(dir.FullName, "Directory.Packages.props");
                if (!File.Exists(props)) continue;
                foreach (var item in XDocument.Load(props).Descendants().Where(e => e.Name.LocalName == "PackageVersion"))
                    if ((item.Attribute("Include") ?? item.Attribute("Update")) is { } name) central[name.Value] = Value(item, "Version") ?? "unknown";
                break;
            }
            var graph = new TargetGraph { Name = "Declarations (unevaluated)" };
            foreach (var item in document.Descendants().Where(e => e.Name.LocalName == "PackageReference" && e.Attribute("Include") is not null))
            {
                var id = item.Attribute("Include")!.Value;
                var version = Value(item, "VersionOverride") ?? Value(item, "Version") ?? central.GetValueOrDefault(id) ?? "unknown";
                var key = $"{id}/{version}";
                if (!graph.Packages.Any(p => p.Key == key)) graph.Packages.Add(new(key, id, version, true, version, false));
                graph.Roots.Add(key);
            }
            var legacy = Path.Combine(Path.GetDirectoryName(project.Path)!, "packages.config");
            if (File.Exists(legacy))
            {
                foreach (var item in XDocument.Load(legacy).Descendants("package"))
                {
                    var id = item.Attribute("id")?.Value;
                    if (id is null) continue;
                    var version = item.Attribute("version")?.Value ?? "unknown";
                    graph.Packages.Add(new($"{id}/{version}", id, version, false, version, false));
                }
                diagnostics.Add(new("LEGACY_PACKAGES", "packages.config entries are declarations; direct/transitive classification and dependency chains are unavailable.", project.Id));
            }
            project.Targets.Add(graph);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException)
        {
            diagnostics.Add(new("INVALID_PROJECT", ex.Message, project.Id));
        }
    }

    private static string? Value(XElement element, string name) => element.Attribute(name)?.Value ?? element.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
}

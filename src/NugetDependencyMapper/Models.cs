namespace NugetDependencyMapper;

public sealed class DependencyReport
{
    public string Name { get; set; } = "";
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ProjectInfo> Projects { get; set; } = [];
    public List<PackageInfo> Packages { get; set; } = [];
    public List<Diagnostic> Diagnostics { get; set; } = [];
    public bool IsDemo { get; set; }
}

public sealed class ProjectInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool Resolved { get; set; }
    public List<TargetGraph> Targets { get; set; } = [];
}

public sealed class TargetGraph
{
    public string Name { get; set; } = "";
    public List<PackageUse> Packages { get; set; } = [];
    public List<DependencyEdge> Edges { get; set; } = [];
    // Roots include referenced projects; project libraries have no PackageUse entry.
    public List<string> Roots { get; set; } = [];
    public Dictionary<string, string> ProjectLibraries { get; set; } = [];
}

public sealed record PackageUse(string Key, string Id, string Version, bool Direct, string? Requested, bool Resolved, LicenseInfo? License = null);
public sealed record LicenseInfo(string Kind, string? Value = null, string? Url = null, bool RequireAcceptance = false, string? Note = null)
{
    public static LicenseInfo Unknown(string note) => new("unknown", Note: note);
}
public sealed record VersionLicense(string Version, LicenseInfo License);
public sealed record DependencyEdge(string From, string To, string Requested);
public enum DiagnosticSeverity { Warning, Error }
/// <summary>
/// An analysis note. <see cref="DiagnosticSeverity.Error"/> means the report is missing data it
/// should have had; warnings are advisory and must not, on their own, fail a CI run.
/// </summary>
public sealed record Diagnostic(string Code, string Message, string? Project = null, DiagnosticSeverity Severity = DiagnosticSeverity.Warning);
public sealed record Usage(string Project, string Target, string Key, bool Direct, string? Requested, bool Resolved);
public sealed class PackageInfo
{
    public string Id { get; set; } = "";
    public List<string> Versions { get; set; } = [];
    public List<Usage> Usages { get; set; } = [];
    public List<VersionLicense> Licenses { get; set; } = [];
    public bool HasVersionDrift => Versions.Count > 1;
}

public static class ReportIndex
{
    public static void Build(DependencyReport report)
    {
        report.Packages = report.Projects.SelectMany(p => p.Targets.SelectMany(t =>
                t.Packages.Select(u => (Package: u, Use: new Usage(p.Id, t.Name, u.Key, u.Direct, u.Requested, u.Resolved)))))
            .GroupBy(x => x.Package.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PackageInfo
            {
                Id = g.First().Package.Id,
                Versions = g.Where(x => x.Package.Resolved).Select(x => x.Package.Version)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList(),
                Usages = g.Select(x => x.Use).ToList(),
                Licenses = g.Where(x => x.Package.Resolved)
                    .Select(x => new VersionLicense(x.Package.Version, x.Package.License ?? LicenseInfo.Unknown("Local license metadata is unavailable.")))
                    .Distinct().OrderBy(x => x.Version, StringComparer.OrdinalIgnoreCase).ToList()
            }).ToList();
    }
}

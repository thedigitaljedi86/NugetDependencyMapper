using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace NugetDependencyMapper;

public static partial class Discovery
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".csproj", ".fsproj", ".vbproj" };
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", "node_modules", "artifacts", ".tools" };
    public static List<string> Find(string input)
    {
        input = Path.GetFullPath(input);
        var projects = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (Directory.Exists(input)) Walk(input, projects);
        else if (!File.Exists(input)) throw new FileNotFoundException($"Input does not exist: {input}");
        else if (Extensions.Contains(Path.GetExtension(input))) projects.Add(input);
        else if (Path.GetExtension(input).Equals(".sln", StringComparison.OrdinalIgnoreCase))
        {
            foreach (Match match in SolutionProject().Matches(File.ReadAllText(input))) AddRelative(input, match.Groups[1].Value, projects);
        }
        else if (Path.GetExtension(input).Equals(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var item in XDocument.Load(input).Descendants().Where(e => e.Name.LocalName == "Project"))
                if (item.Attribute("Path") is { } path) AddRelative(input, path.Value, projects);
        }
        else throw new ArgumentException("Expected a folder, .sln, .slnx, .csproj, .fsproj or .vbproj file.");

        // Follow literal project references, including those outside the selected folder.
        var queue = new Queue<string>(projects);
        while (queue.TryDequeue(out var project))
        {
            if (!File.Exists(project)) continue;
            XDocument document;
            try { document = XDocument.Load(project); }
            catch (System.Xml.XmlException) { continue; }
            foreach (var reference in document.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
            {
                var path = reference.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(path) || path.Contains('$') || path.Contains('*')) continue;
                var resolved = Relative(project, path);
                if (Extensions.Contains(Path.GetExtension(resolved)) && projects.Add(resolved)) queue.Enqueue(resolved);
            }
        }
        return projects.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void Walk(string directory, HashSet<string> projects)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
            if (Extensions.Contains(Path.GetExtension(file))) projects.Add(Path.GetFullPath(file));
        foreach (var child in Directory.EnumerateDirectories(directory))
            if (!Excluded.Contains(Path.GetFileName(child)) && !File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint)) Walk(child, projects);
    }

    public static string Relative(string file, string relative) => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
    private static void AddRelative(string input, string path, HashSet<string> projects)
    {
        if (Extensions.Contains(Path.GetExtension(path))) projects.Add(Relative(input, path));
    }

    [GeneratedRegex("^Project\\([^\\r\\n]*?=\\s*\"[^\"]*\",\\s*\"([^\"]+)\"", RegexOptions.Multiline)]
    private static partial Regex SolutionProject();
}

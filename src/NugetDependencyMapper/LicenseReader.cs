using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace NugetDependencyMapper;

// Read publisher metadata from the exact restored package, never infer usage rights.
public sealed class LicenseReader
{
    private readonly Dictionary<string, LicenseInfo> cache = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public LicenseInfo Read(JsonElement assets, string key, string id)
    {
        if (!assets.TryGetProperty("libraries", out var libraries) || !libraries.TryGetProperty(key, out var library) ||
            !library.TryGetProperty("path", out var packagePath) || !assets.TryGetProperty("packageFolders", out var folders))
            return LicenseInfo.Unknown("Package cache location is absent from the restore data.");

        try
        {
            foreach (var folder in folders.EnumerateObject())
            {
                var packageDirectory = ContainedPath(folder.Name, packagePath.GetString() ?? "");
                if (packageDirectory is null || !Directory.Exists(packageDirectory)) continue;
                // NuGet normally extracts the lower-case ID as the nuspec filename.
                var nuspec = ContainedPath(packageDirectory, id.ToLowerInvariant() + ".nuspec");
                if (nuspec is null || !File.Exists(nuspec))
                {
                    var listed = library.TryGetProperty("files", out var files) ? files.EnumerateArray()
                        .Select(f => f.GetString()).FirstOrDefault(f => f?.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) == true) : null;
                    nuspec = listed is null ? null : ContainedPath(packageDirectory, listed);
                }
                if (nuspec is null || !File.Exists(nuspec)) continue;
                if (!cache.TryGetValue(nuspec, out var license)) cache[nuspec] = license = ReadNuspec(nuspec, id);
                return license;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return LicenseInfo.Unknown("Local package metadata could not be read.");
        }
        return LicenseInfo.Unknown("The restored package's .nuspec is not available in the local cache. Run --restore to download missing packages.");
    }

    private static LicenseInfo ReadNuspec(string path, string expectedId)
    {
        try
        {
            using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2_000_000 });
            var document = XDocument.Load(reader);
            var metadata = document.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata");
            string? Value(string name) => metadata?.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();
            if (!string.Equals(Value("id"), expectedId, StringComparison.OrdinalIgnoreCase))
                return LicenseInfo.Unknown("The local nuspec package identity does not match the restored package.");
            var acceptance = bool.TryParse(Value("requireLicenseAcceptance"), out var accept) && accept;
            var license = metadata?.Elements().FirstOrDefault(e => e.Name.LocalName == "license");
            var value = license?.Value.Trim();
            if (!string.IsNullOrEmpty(value))
            {
                if (license!.Attribute("type")?.Value == "expression")
                    return new("expression", value, "https://licenses.nuget.org/" + Uri.EscapeDataString(value), acceptance);
                if (license!.Attribute("type")?.Value == "file")
                    return new("file", value, RequireAcceptance: acceptance, Note: "Review this license file inside the package; its contents are not embedded in this report.");
                return new("unknown", RequireAcceptance: acceptance, Note: "The package declares an unsupported license metadata type.");
            }
            var url = Value("licenseUrl");
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
                return new("url", Url: uri.AbsoluteUri, RequireAcceptance: acceptance, Note: "Legacy license link. The linked terms have not been fetched or classified.");
            return new("unknown", RequireAcceptance: acceptance, Note: "The nuspec does not declare a license expression, file or usable license URL.");
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return LicenseInfo.Unknown("The local nuspec is unreadable or invalid.");
        }
    }

    private static string? ContainedPath(string directory, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) return null;
        var root = Path.GetFullPath(directory);
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('\\', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return null;
        // Do not follow metadata-supplied symlinks out of the package cache.
        var current = root;
        foreach (var segment in Path.GetRelativePath(root, full).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) return null;
        }
        return full;
    }
}

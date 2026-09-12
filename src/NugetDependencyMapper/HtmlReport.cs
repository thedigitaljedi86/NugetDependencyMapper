using System.Reflection;
using System.Text.Json;

namespace NugetDependencyMapper;

public static class HtmlReport
{
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };
    public static string Json(DependencyReport report) => JsonSerializer.Serialize(report, JsonOptions);
    public static string Render(DependencyReport report)
    {
        // System.Text.Json's default encoder escapes HTML characters, including script terminators.
        return Resource("index.html").Replace("/*__STYLE__*/", Resource("style.css"))
            .Replace("/*__APP__*/", Resource("app.js")).Replace("/*__DATA__*/", Json(report));
    }
    private static string Resource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"NugetDependencyMapper.Report.{name}")
            ?? throw new InvalidOperationException($"Missing embedded report resource: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

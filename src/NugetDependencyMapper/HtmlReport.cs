using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NugetDependencyMapper;

public static class HtmlReport
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
    public static string Json(DependencyReport report) => JsonSerializer.Serialize(report, JsonOptions);
    public static string Render(DependencyReport report)
    {
        // System.Text.Json's default encoder escapes HTML characters, including script terminators.
        var style = Resource("style.css")
            .Replace("/*__FONT_DATA__*/", FontData())
            .Replace("/*__FONT_LICENSE__*/", $"/*\n{Resource("Fonts.OpenSans.OFL.txt")}\n*/");
        return Resource("index.html").Replace("/*__STYLE__*/", style)
            .Replace("/*__APP__*/", Resource("app.js")).Replace("/*__DATA__*/", Json(report));
    }
    private static string FontData()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NugetDependencyMapper.Report.Fonts.OpenSans.ttf")
            ?? throw new InvalidOperationException("Missing embedded Open Sans font.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Convert.ToBase64String(buffer.ToArray());
    }
    private static string Resource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"NugetDependencyMapper.Report.{name}")
            ?? throw new InvalidOperationException($"Missing embedded report resource: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

using System.Text.Json;
using NugetDependencyMapper;
using Cli = NugetDependencyMapper.Program;

// Must run before anything touches RetroConsole: the CLI tests call Program.Main directly, and the
// full-screen chrome would otherwise repaint the terminal and wait for ENTER on a developer machine.
Environment.SetEnvironmentVariable("NUGET_MAP_PLAIN", "1");

var tests = new (string Name, Func<Task> Run)[]
{
    ("Restored graph preserves transitive chains and project edges", () => Check(f =>
    {
        var path = f.Project("App"); f.Assets(path);
        var report = Analyzer.Analyze(path);
        var graph = report.Projects.Single().Targets.Single();
        Assert(report.Projects.Single().Resolved, "must be resolved");
        Assert(graph.Packages.Single(p => p.Id == "Root").Direct, "root must be direct");
        Assert(!graph.Packages.Single(p => p.Id == "Leaf").Direct, "leaf must be transitive");
        Assert(graph.Edges.Any(e => e.From == "Root/1.0.0" && e.To == "Middle/1.0.0"), "first chain edge");
        Assert(graph.Edges.Any(e => e.From == "Middle/1.0.0" && e.To == "Leaf/1.0.0"), "second chain edge");
        Assert(graph.Roots.Contains("Shared/1.0.0"), "project reference entry point");
        Assert(!report.Packages.Any(p => p.Id == "Shared"), "project must not become a NuGet package");
    })),
    ("Multi-target and RID graphs stay separate", () => Check(f =>
    {
        var path = f.Project("App"); f.Assets(path, multiTarget: true);
        var report = Analyzer.Analyze(path);
        Assert(report.Projects.Single().Targets.Count == 3, "three target graphs");
        Assert(report.Packages.Single(p => p.Id == "Leaf").HasVersionDrift, "drift between target graphs");
        Assert(report.Projects.Single().Targets.All(t => t.Packages.Single(p => p.Id == "Root").Direct), "direct classification in RID target");
        Assert(report.Projects.Single().Targets.Single(t => t.Name == "net8.0").Packages.Single(p => p.Id == "Leaf").Version == "1.0.0", "no target version leakage");
    })),
    ("Package identity is case insensitive across projects", () => Check(f =>
    {
        var a = f.Project("A"); var b = f.Project("B"); f.Assets(a); f.Assets(b, leafId: "leaf", leafVersion: "2.0.0");
        var report = Analyzer.Analyze(f.Root);
        Assert(report.Packages.Count(p => p.Id.Equals("Leaf", StringComparison.OrdinalIgnoreCase)) == 1, "merge casing");
        Assert(report.Packages.Single(p => p.Id.Equals("Leaf", StringComparison.OrdinalIgnoreCase)).Versions.Count == 2, "both versions retained");
    })),
    ("Fallback reads central declarations without claiming installed versions", () => Check(f =>
    {
        f.Write("Directory.Packages.props", "<Project><ItemGroup><PackageVersion Include=\"Root\" Version=\"[1.0,2.0)\" /></ItemGroup></Project>");
        var path = f.Project("App", "<PackageReference Include=\"Root\" /><PackageReference Include=\"Override\" VersionOverride=\"3.0.0\" />");
        var report = Analyzer.Analyze(path);
        Assert(!report.Projects.Single().Resolved, "fallback is partial");
        Assert(report.Packages.All(p => p.Versions.Count == 0), "no false resolved versions");
        Assert(report.Projects.Single().Targets.Single().Packages.Single(p => p.Id == "Root").Requested == "[1.0,2.0)", "central declared range");
        Assert(report.Projects.Single().Targets.Single().Packages.Single(p => p.Id == "Override").Version == "3.0.0", "version override");
        Assert(report.Diagnostics.Any(d => d.Code == "DECLARED_ONLY"), "explicit coverage warning");
    })),
    ("Missing and malformed assets produce an explicit partial report", () => Check(f =>
    {
        var path = f.Project("App", "<PackageReference Include=\"Root\" Version=\"1.0\" />");
        f.Write("App/obj/project.assets.json", "{not valid}");
        var report = Analyzer.Analyze(path);
        Assert(report.Diagnostics.Any(d => d.Code == "INVALID_ASSETS"), "bad assets diagnostic");
        Assert(report.Packages.Single().Id == "Root", "declaration preserved");
    })),
    ("Assets ownership is validated", () => Check(f =>
    {
        var path = f.Project("App"); f.Assets(path, owner: Path.Combine(f.Root, "Other.csproj"));
        var report = Analyzer.Analyze(path);
        Assert(!report.Projects.Single().Resolved, "foreign assets rejected");
        Assert(report.Diagnostics.Any(d => d.Code == "INVALID_ASSETS"), "foreign assets diagnostic");
    })),
    ("Custom assets paths are indexed by project identity", () => Check(f =>
    {
        var path = f.Project("App"); f.Assets(path);
        Directory.CreateDirectory(Path.Combine(f.Root, "custom"));
        File.Move(Path.Combine(f.Root, "App/obj/project.assets.json"), Path.Combine(f.Root, "custom/project.assets.json"));
        Assert(Analyzer.Analyze(path, Path.Combine(f.Root, "custom")).Projects.Single().Resolved, "custom location found");
    })),
    ("Changed central settings are reported as stale", () => Check(f =>
    {
        var path = f.Project("App"); f.Assets(path); f.Write("Directory.Packages.props", "<Project />");
        File.SetLastWriteTimeUtc(Path.Combine(f.Root, "Directory.Packages.props"), DateTime.UtcNow.AddMinutes(1));
        Assert(Analyzer.Analyze(path).Diagnostics.Any(d => d.Code == "STALE_RESTORE"), "central settings staleness");
    })),
    ("Solutions honor membership and Windows separators", () => Check(f =>
    {
        var a = f.Project("A"); f.Project("Unrelated");
        var solution = f.Write("App.sln", "Microsoft Visual Studio Solution File, Format Version 12.00\nProject(\"{TYPE}\") = \"A\", \"A\\A.csproj\", \"{GUID}\"\nEndProject\n");
        Assert(Discovery.Find(solution).SequenceEqual([a]), "sln membership");
        var slnx = f.Write("App.slnx", "<Solution><Folder Name=\"/src/\"><Project Path=\"A/A.csproj\" /></Folder></Solution>");
        Assert(Discovery.Find(slnx).SequenceEqual([a]), "slnx membership");
    })),
    ("Discovery follows project references and excludes build folders", () => Check(f =>
    {
        var a = f.Project("A", "<ProjectReference Include=\"../B/B.csproj\" />"); f.Project("B", "<ProjectReference Include=\"../A/A.csproj\" />");
        f.Write("obj/Generated.csproj", "<Project />"); f.Write("node_modules/Fake.csproj", "<Project />");
        Assert(Discovery.Find(a).Count == 2, "reference closure and cycle guard");
        Assert(Discovery.Find(f.Root).Count == 2, "excluded folders");
    })),
    ("Missing referenced projects are retained as diagnostics", () => Check(f =>
    {
        var path = f.Project("A", "<ProjectReference Include=\"../Missing/Missing.csproj\" />");
        var report = Analyzer.Analyze(path);
        Assert(report.Projects.Count == 2 && report.Diagnostics.Any(d => d.Code == "MISSING_PROJECT"), "missing reference visible");
    })),
    ("HTML safely embeds script terminators and contains no remote resources", () => Check(_ =>
    {
        var report = Demo.Create(); report.Name = "</script><script>alert('x')</script> & \"";
        var html = HtmlReport.Render(report);
        Assert(!html.Contains(report.Name), "unsafe text must be encoded");
        Assert(html.Contains("\\u003C/script\\u003E"), "JSON encoder escapes HTML");
        Assert(!html.Contains("/*__"), "all template tokens replaced");
        Assert(!html.Contains("<script src=") && !html.Contains("@import"), "offline report");
        Assert(html.Contains("<svg"), "graph included");
    })),
    ("CLI reports drift and incomplete analysis with usable artifacts", async () =>
    {
        using var f = new Fixture();
        var output = Path.Combine(f.Root, "demo.html");
        Assert(await Cli.Main(["--demo", "--name", "Test report", "-o", output, "--fail-on-drift"]) == 2, "drift exit code");
        Assert(File.Exists(output), "report exists despite drift exit");
        var project = f.Project("App");
        Assert(await Cli.Main([project, "--name", "Test report", "-o", output, "--fail-on-incomplete"]) == 3, "incomplete exit code");
        Assert(await Cli.Main(["--unknown"]) == 1, "bad option exit code");
        Assert(await Cli.Main(["--output"]) == 1, "missing option value exit code");
        Assert(await Cli.Main([project, "-o", project]) == 1, "cannot overwrite project file");
    }),
    ("Report naming prompts again for blank input and handles end of input", () => Check(_ =>
    {
        using var input = new StringReader("  \n  Platform dependencies  \n");
        using var output = new StringWriter();
        Assert(Cli.ReadReportName(input, output) == "Platform dependencies", "trimmed user-entered title");
        Assert(output.ToString().Contains("Enter a non-empty report name."), "blank input prompts again");
        using var empty = new StringReader("");
        try { Cli.ReadReportName(empty, output); throw new Exception("Expected missing name failure"); }
        catch (InvalidOperationException ex) { Assert(ex.Message.Contains("--name"), "EOF has actionable error"); }
    })),
    ("Explicit report title is retained in HTML and JSON", async () =>
    {
        using var f = new Fixture();
        var html = Path.Combine(f.Root, "custom.html");
        var json = Path.Combine(f.Root, "custom.json");
        const string name = "Platform <review> & dependencies";
        Assert(await Cli.Main(["--demo", "--name", name, "-o", html, "--json", json]) == 0, "named generation without prompt");
        using var document = JsonDocument.Parse(File.ReadAllText(json));
        Assert(document.RootElement.GetProperty("name").GetString() == name, "name preserved in JSON");
        var report = File.ReadAllText(html);
        Assert(report.Contains("Platform \\u003Creview\\u003E \\u0026 dependencies"), "name safely embedded in HTML");
        Assert(await Cli.Main(["--demo", "--name", "  ", "-o", html]) == 1, "explicit blank name rejected");
        Assert(await Cli.Main(["--name"]) == 1, "missing name argument rejected");
    }),
    ("Older long framework names use matching dependency groups", () => Check(f =>
    {
        var path = f.Project("App"); f.Assets(path);
        var assets = Path.Combine(Path.GetDirectoryName(path)!, "obj/project.assets.json");
        var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(assets))!;
        var graph = document["targets"]!["net8.0"]!.DeepClone();
        document["targets"] = new System.Text.Json.Nodes.JsonObject { [".NETCoreApp,Version=v8.0"] = graph };
        document["projectFileDependencyGroups"] = new System.Text.Json.Nodes.JsonObject
        {
            [".NETCoreApp,Version=v8.0"] = new System.Text.Json.Nodes.JsonArray("Root >= 1.0.0")
        };
        File.WriteAllText(assets, document.ToJsonString());
        var report = Analyzer.Analyze(path);
        Assert(report.Projects.Single().Targets.Single().Packages.Single(p => p.Id == "Root").Direct, "long framework root");
        Assert(!report.Diagnostics.Any(d => d.Code == "TARGET_METADATA"), "matching dependency group found");
    })),
    ("License expressions are read offline and preserved per resolved version", () => Check(f =>
    {
        var a = f.Project("A"); var b = f.Project("B"); f.Assets(a); f.Assets(b, leafVersion: "2.0.0");
        f.License(a, "Leaf/1.0.0", "<license type=\"expression\">MIT OR Apache-2.0</license>");
        f.License(b, "Leaf/2.0.0", "<license type=\"expression\">Apache-2.0</license>");
        var report = Analyzer.Analyze(f.Root);
        var licenses = report.Packages.Single(p => p.Id == "Leaf").Licenses;
        Assert(licenses.Count == 2, "metadata for each version");
        Assert(licenses.Single(l => l.Version == "1.0.0").License.Value == "MIT OR Apache-2.0", "compound expression preserved");
        Assert(licenses.Single(l => l.Version == "2.0.0").License.Value == "Apache-2.0", "second version independent");
        Assert(licenses.All(l => l.License.Url!.StartsWith("https://licenses.nuget.org/")), "expression links");
    })),
    ("License file takes precedence over legacy URL and preserves acceptance", () => Check(f =>
    {
        var path = f.Project("App"); f.Assets(path);
        f.License(path, "Root/1.0.0", "<license type=\"file\">docs/LICENSE.txt</license><licenseUrl>https://example.org/old-license</licenseUrl><requireLicenseAcceptance>true</requireLicenseAcceptance>");
        var license = Analyzer.Analyze(path).Packages.Single(p => p.Id == "Root").Licenses.Single().License;
        Assert(license.Kind == "file" && license.Value == "docs/LICENSE.txt", "file metadata");
        Assert(license.Url is null && license.RequireAcceptance, "precedence and acceptance metadata");
    })),
    ("Missing, invalid and unsafe license metadata remains unknown", () => Check(f =>
    {
        var path = f.Project("App"); f.Assets(path);
        f.License(path, "Root/1.0.0", "<licenseUrl>javascript:alert(1)</licenseUrl>");
        f.License(path, "Middle/1.0.0", "<license type=\"expression\">unclosed");
        var report = Analyzer.Analyze(path);
        Assert(report.Projects.Single().Resolved, "bad license metadata must not discard the graph");
        Assert(report.Packages.All(p => p.Licenses.All(l => l.License.Kind == "unknown")), "no invented licenses");
        Assert(report.Packages.All(p => p.Licenses.All(l => l.License.Url is null)), "no unsafe links");
    })),
    ("Legacy HTTPS license URLs are retained without classification", () => Check(f =>
    {
        var path = f.Project("App"); f.Assets(path);
        f.License(path, "Root/1.0.0", "<licenseUrl>https://example.org/terms</licenseUrl>");
        var license = Analyzer.Analyze(path).Packages.Single(p => p.Id == "Root").Licenses.Single().License;
        Assert(license.Kind == "url" && license.Url == "https://example.org/terms", "legacy metadata");
    })),
    ("Package metadata cannot traverse outside the recorded cache", () => Check(f =>
    {
        var path = f.Project("App"); f.Assets(path);
        f.License(path, "Root/1.0.0", "<license type=\"expression\">MIT</license>");
        f.Write("outside/root.nuspec", "<package><metadata><id>Root</id><license type=\"expression\">Forbidden</license></metadata></package>");
        var assets = Path.Combine(Path.GetDirectoryName(path)!, "obj/project.assets.json");
        var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(assets))!;
        document["libraries"]!["Root/1.0.0"]!["path"] = "../outside";
        File.WriteAllText(assets, document.ToJsonString());
        var license = Analyzer.Analyze(path).Packages.Single(p => p.Id == "Root").Licenses.Single().License;
        Assert(license.Kind == "unknown", "unsafe cache-relative path rejected");
    })),
    ("Many nested repositories with identical project names stay distinct", () => Check(f =>
    {
        for (var i = 0; i < 40; i++)
        {
            var path = f.Write($"team-{i}/repo/src/App/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            f.Assets(path, leafVersion: i % 2 == 0 ? "1.0.0" : "2.0.0");
        }
        f.Write("team-0/repo/obj/Generated.csproj", "<Project />");
        var report = Analyzer.Analyze(f.Root);
        Assert(report.Projects.Count == 40 && report.Projects.All(p => p.Name == "App"), "all nested projects retained, generated project excluded");
        Assert(report.Projects.Select(p => p.Id).Distinct().Count() == 40, "relative paths distinguish project identity");
        Assert(report.Packages.Single(p => p.Id == "Leaf").HasVersionDrift, "drift across repositories");
        Assert(report.Packages.Single(p => p.Id == "Leaf").Usages.Count == 40, "all reverse usages retained");
    })),
    ("Version is read from the assembly, not a hard-coded literal", () => Check(_ =>
    {
        Assert(System.Version.TryParse(Cli.Version.Split('-', '+')[0], out var parsed) && parsed.Major + parsed.Minor > 0,
            $"parsable assembly version, got '{Cli.Version}'");
        Assert(Options.HelpText.Contains("--version"), "version is documented");
    })),
    ("NuGet warnings are reported but do not fail an otherwise complete run", async () =>
    {
        using var f = new Fixture();
        var path = f.Project("App"); f.Assets(path);
        f.Log(path, "NU1603", "Warning", "App depends on Leaf (>= 1.0.0) but Leaf 1.0.0 was not found.");
        var report = Analyzer.Analyze(path);
        Assert(report.Diagnostics.Single(d => d.Code == "NU1603").Severity == DiagnosticSeverity.Warning, "NuGet warning level honoured");
        Assert(await Cli.Main([path, "--name", "W", "-o", Path.Combine(f.Root, "w.html"), "--fail-on-incomplete"]) == 0, "warnings alone must not fail CI");
    }),
    ("NuGet errors still fail an incomplete run", async () =>
    {
        using var f = new Fixture();
        var path = f.Project("App"); f.Assets(path);
        f.Log(path, "NU1101", "Error", "Unable to find package Leaf.");
        Assert(Analyzer.Analyze(path).Diagnostics.Single(d => d.Code == "NU1101").Severity == DiagnosticSeverity.Error, "NuGet error level honoured");
        Assert(await Cli.Main([path, "--name", "E", "-o", Path.Combine(f.Root, "e.html"), "--fail-on-incomplete"]) == 3, "error-level notes fail the run");
    }),
    ("Dependencies outside the restored graph are summarised once per target", () => Check(f =>
    {
        var path = f.Project("App"); f.Assets(path);
        var assets = Path.Combine(Path.GetDirectoryName(path)!, "obj/project.assets.json");
        var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(assets))!;
        var dependencies = document["targets"]!["net8.0"]!["Root/1.0.0"]!["dependencies"]!.AsObject();
        foreach (var absent in new[] { "System.Runtime", "System.Text.Json", "System.Memory", "System.Buffers", "System.Threading", "System.Linq" })
            dependencies[absent] = "1.0.0";
        File.WriteAllText(assets, document.ToJsonString());
        var unresolved = Analyzer.Analyze(path).Diagnostics.Where(d => d.Code == "UNRESOLVED_EDGE").ToList();
        Assert(unresolved.Count == 1, $"one aggregated note per target, got {unresolved.Count}");
        Assert(unresolved[0].Severity == DiagnosticSeverity.Warning, "framework-provided dependencies are not an error");
        Assert(unresolved[0].Message.Contains("6 dependencies") && unresolved[0].Message.Contains("System.Buffers") && unresolved[0].Message.EndsWith("PrivateAssets/ExcludeAssets."),
            $"counted and truncated summary: {unresolved[0].Message}");
    })),
    ("packages.config entries stay visible as direct declarations", () => Check(f =>
    {
        var path = f.Project("App");
        f.Write("App/packages.config", "<packages><package id=\"Newtonsoft.Json\" version=\"12.0.3\" /><package id=\"Newtonsoft.Json\" version=\"12.0.3\" /></packages>");
        var graph = Analyzer.Analyze(path).Projects.Single().Targets.Single();
        var use = graph.Packages.Single(p => p.Id == "Newtonsoft.Json");
        Assert(use.Direct && !use.Resolved, "declared directly, but never claimed as installed");
        Assert(graph.Roots.Count(r => r == use.Key) == 1, "a single graph root per declaration");
    })),
    ("Diagnostic severity reaches the report as a readable string", () => Check(_ =>
    {
        var report = Demo.Create();
        report.Diagnostics.Add(new("RESTORE_FAILED", "boom", "a.csproj", DiagnosticSeverity.Error));
        report.Diagnostics.Add(new("STALE_RESTORE", "old", "a.csproj"));
        var json = HtmlReport.Json(report);
        Assert(json.Contains("\"severity\":\"error\"") && json.Contains("\"severity\":\"warning\""), "severity is exported as a string the report can read");
    })),
    ("Terminal chrome fits every window it claims to support", () => Check(_ =>
    {
        Assert(!RetroConsole.SupportsChrome(39, 40), "too narrow for the frame");
        Assert(!RetroConsole.SupportsChrome(80, 11), "too short for the frame");
        Assert(RetroConsole.SupportsChrome(40, 12), "smallest supported window");
        for (var width = 40; width <= 200; width++)
        {
            // DrawContent indents by two and Fit clips anything reaching the right edge.
            var bar = $"[{RetroConsole.Bar(7, 300, width)}] {RetroConsole.Fit("100", 3)}%";
            Assert(("  " + bar).Length < width, $"progress bar fits at width {width}: {bar.Length + 2} columns");
            var line = RetroConsole.Line("Scanning:", "/a/very/long/path/SomeVeryLongProjectName.csproj", "(151 of 300)", width);
            Assert(("  " + line).Length < width, $"scan line fits at width {width}: '{line}'");
            Assert(line.EndsWith("(151 of 300)"), $"the counter survives truncation at width {width}: '{line}'");
        }
    })),
    ("Parse failures are listed last, after everything else the run prints", async () =>
    {
        using var f = new Fixture();
        var broken = f.Project("Broken", "<PackageReference Include=\"Root\" Version=\"1.0\" />");
        f.Write("Broken/obj/project.assets.json", "{not valid}");
        var ok = f.Project("Ok"); f.Assets(ok);
        f.Log(ok, "NU1603", "Warning", "A restore warning that must not bury the error.");
        var output = Path.Combine(f.Root, "e.html");
        var (log, code) = await Capture(() => Cli.Main([f.Root, "--name", "E", "-o", output]));
        Assert(code == 0, "a parse failure alone does not fail the run");
        var list = log.IndexOf("1. [INVALID_ASSETS]", StringComparison.Ordinal);
        Assert(list > 0, $"errors are listed with a number:\n{log}");
        Assert(log.Contains("1 error:"), $"the list is headed with a count:\n{log}");
        Assert(list > log.IndexOf("warning [NU1603]", StringComparison.Ordinal), "warnings come before the error list");
        Assert(list > log.IndexOf("Report: ", StringComparison.Ordinal), "the report path comes before the error list");
        Assert(list > log.IndexOf("Mapped ", StringComparison.Ordinal), "the summary comes before the error list");
        // The exact wording of the runtime's JSON exception is not ours to pin, only its placement.
        var lines = log.ReplaceLineEndings("\n").TrimEnd().Split('\n');
        Assert(lines[^1].StartsWith("      Cannot read restored dependencies:", StringComparison.Ordinal),
            $"the error message is the very last line:\n{log}");
        Assert(lines[^2].Contains("1. [INVALID_ASSETS] Broken/Broken.csproj", StringComparison.Ordinal),
            $"its heading is immediately above:\n{log}");
    }),
    ("A fatal exception is reported through the same final list", async () =>
    {
        using var f = new Fixture();
        var (log, code) = await Capture(() => Cli.Main(["--demo", "--name", "E", "-o", Path.Combine(f.Root, "report.txt")]));
        Assert(code == 1, "invalid output extension fails the run");
        Assert(log.Contains("1 error:") && log.Contains("1. [ArgumentException] Output must use the .html extension"),
            $"the exception is listed like any other error:\n{log}");
        Assert(log.TrimEnd().EndsWith(".html extension: " + Path.Combine(f.Root, "report.txt")), $"nothing follows the list:\n{log}");
    }),
    ("No projects is an error, not an empty successful report", () => Check(f =>
    {
        try { Analyzer.Analyze(f.Root); throw new Exception("Expected failure"); }
        catch (InvalidOperationException ex) { Assert(ex.Message.Contains("No .NET projects"), "helpful failure message"); }
    }))
};
var failures = 0;
foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static Task Check(Action<Fixture> run) { using var fixture = new Fixture(); run(fixture); return Task.CompletedTask; }

/// <summary>Runs the CLI with stdout and stderr merged, so the order they were written in is testable.</summary>
static async Task<(string Log, int Code)> Capture(Func<Task<int>> run)
{
    var (stdout, stderr) = (Console.Out, Console.Error);
    var log = new StringWriter();
    Console.SetOut(log); Console.SetError(log);
    try
    {
        var code = await run();
        return (log.ToString(), code);
    }
    finally { Console.SetOut(stdout); Console.SetError(stderr); }
}
static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }

sealed class Fixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "nuget-map-test-" + Guid.NewGuid().ToString("N"));
    public Fixture() => Directory.CreateDirectory(Root);
    public string Write(string relative, string content)
    {
        var path = Path.Combine(Root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content); return path;
    }
    public string Project(string name, string items = "") => Write($"{name}/{name}.csproj", $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup>{items}</ItemGroup></Project>");
    public void Assets(string path, bool multiTarget = false, string leafId = "Leaf", string leafVersion = "1.0.0", string? owner = null)
    {
        Dictionary<string, object> Graph(string leaf) => new()
        {
            ["Root/1.0.0"] = new { type = "package", dependencies = new Dictionary<string, string> { ["Middle"] = "1.0.0" } },
            ["Middle/1.0.0"] = new { type = "package", dependencies = new Dictionary<string, string> { [leafId] = leaf } },
            [$"{leafId}/{leaf}"] = new { type = "package" },
            ["Shared/1.0.0"] = new { type = "project", dependencies = new Dictionary<string, string> { [leafId] = leaf } }
        };
        var targets = new Dictionary<string, object> { ["net8.0"] = Graph(leafVersion) };
        if (multiTarget) { targets["net9.0"] = Graph("2.0.0"); targets["net8.0/linux-x64"] = Graph(leafVersion); }
        var framework = new { dependencies = new Dictionary<string, object> { ["Root"] = new { target = "Package", version = "[1.0.0, )" } } };
        var document = new
        {
            version = 3, targets,
            project = new { restore = new { projectPath = owner ?? path }, frameworks = new Dictionary<string, object> { ["net8.0"] = framework, ["net9.0"] = framework } }
        };
        var assetsPath = Path.Combine(Path.GetDirectoryName(path)!, "obj/project.assets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(assetsPath)!); File.WriteAllText(assetsPath, JsonSerializer.Serialize(document));
    }
    public void Log(string project, string code, string level, string message)
    {
        var assets = Path.Combine(Path.GetDirectoryName(project)!, "obj/project.assets.json");
        var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(assets))!;
        var logs = document["logs"]?.AsArray() ?? new System.Text.Json.Nodes.JsonArray();
        logs.Add(new System.Text.Json.Nodes.JsonObject { ["code"] = code, ["level"] = level, ["message"] = message });
        document["logs"] = logs;
        File.WriteAllText(assets, document.ToJsonString());
    }

    public void Dispose() => Directory.Delete(Root, true);
    public void License(string project, string key, string metadata)
    {
        var assets = Path.Combine(Path.GetDirectoryName(project)!, "obj/project.assets.json");
        var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(assets))!;
        var parts = key.Split('/');
        var relative = key.ToLowerInvariant();
        document["packageFolders"] = new System.Text.Json.Nodes.JsonObject { [Path.Combine(Root, "cache")] = new System.Text.Json.Nodes.JsonObject() };
        document["libraries"] ??= new System.Text.Json.Nodes.JsonObject();
        document["libraries"]![key] = new System.Text.Json.Nodes.JsonObject { ["path"] = relative };
        Write($"cache/{relative}/{parts[0].ToLowerInvariant()}.nuspec", $"<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\"><metadata><id>{parts[0]}</id><version>{parts[1]}</version>{metadata}</metadata></package>");
        File.WriteAllText(assets, document.ToJsonString());
    }
}

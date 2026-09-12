using System.Diagnostics;
using System.Text;

namespace NugetDependencyMapper;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            if (options.Help) { Console.WriteLine(Options.HelpText); return 0; }
            if (options.Version) { Console.WriteLine("nuget-map 0.1.0"); return 0; }
            var output = Path.GetFullPath(options.Output);
            var json = options.Json is null ? null : Path.GetFullPath(options.Json);
            ValidateOutput(output, ".html");
            if (json is not null) ValidateOutput(json, ".json");
            if (json == output) throw new ArgumentException("HTML and JSON output must have different paths.");
            if (options.Name is null && Console.IsInputRedirected)
                throw new ArgumentException("Specify --name <report name> when running without an interactive terminal.");
            var reportName = options.Name ?? ReadReportName(Console.In, Console.Out);
            if (options.Restore && !options.Demo)
            {
                var projects = Discovery.Find(options.Input);
                var inputs = Directory.Exists(options.Input) ? projects : [Path.GetFullPath(options.Input)];
                foreach (var input in inputs)
                {
                    Console.WriteLine($"Restoring {input}...");
                    var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(input)! };
                    start.ArgumentList.Add("restore");
                    start.ArgumentList.Add(input);
                    using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start dotnet restore.");
                    await process.WaitForExitAsync();
                    if (process.ExitCode != 0) throw new InvalidOperationException($"dotnet restore failed with exit code {process.ExitCode}. No report was generated.");
                }
            }
            var report = options.Demo ? Demo.Create() : Analyzer.Analyze(options.Input, options.AssetsRoot);
            report.Name = reportName;
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await File.WriteAllTextAsync(output, HtmlReport.Render(report), new UTF8Encoding(false));
            if (json is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(json)!);
                await File.WriteAllTextAsync(json, HtmlReport.Json(report), new UTF8Encoding(false));
            }
            var drift = report.Packages.Count(p => p.HasVersionDrift);
            Console.WriteLine($"Mapped {report.Projects.Count} projects and {report.Packages.Count} packages. {drift} packages have version drift.");
            foreach (var diagnostic in report.Diagnostics) Console.Error.WriteLine($"[{diagnostic.Code}] {diagnostic.Project}: {diagnostic.Message}");
            Console.WriteLine($"Report: {output}");
            if (options.Open)
            {
                try { Process.Start(new ProcessStartInfo(new Uri(output).AbsoluteUri) { UseShellExecute = true }); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Console.Error.WriteLine($"Could not open browser: {ex.Message}"); }
            }
            if (options.FailOnIncomplete && (report.Diagnostics.Count > 0 || report.Projects.Any(p => !p.Resolved))) return 3;
            return options.FailOnDrift && drift > 0 ? 2 : 0;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException or System.Xml.XmlException or System.Text.Json.JsonException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or KeyNotFoundException)
        {
            Console.Error.WriteLine($"nuget-map: {ex.Message}");
            return 1;
        }
    }

    public static string ReadReportName(TextReader input, TextWriter output)
    {
        while (true)
        {
            output.Write("Report name: ");
            output.Flush();
            var name = input.ReadLine();
            if (name is null) throw new InvalidOperationException("No report name was provided. Use --name <report name> to supply it directly.");
            if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
            output.WriteLine("Enter a non-empty report name.");
        }
    }

    private static void ValidateOutput(string path, string extension)
    {
        if (!Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException($"Output must use the {extension} extension: {path}");
        string[] reserved = ["project.assets.json", "global.json", "packages.lock.json", "project.json", "dotnet-tools.json"];
        if (extension == ".json" && reserved.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a report filename instead of a reserved .NET configuration filename.");
    }
}

public sealed class Options
{
    public string Input { get; set; } = ".";
    public string Output { get; set; } = "nuget-map.html";
    public string? Name { get; set; }
    public string? Json { get; set; }
    public string? AssetsRoot { get; set; }
    public bool Restore { get; set; }
    public bool Open { get; set; }
    public bool Demo { get; set; }
    public bool FailOnDrift { get; set; }
    public bool FailOnIncomplete { get; set; }
    public bool Help { get; set; }
    public bool Version { get; set; }

    public static Options Parse(string[] args)
    {
        var result = new Options();
        var hasInput = false;
        for (var i = 0; i < args.Length; i++)
        {
            string Value() => ++i < args.Length && !args[i].StartsWith('-') ? args[i] : throw new ArgumentException($"Missing value for {args[i - 1]}.");
            switch (args[i])
            {
                case "--help": case "-h": result.Help = true; break;
                case "--version": result.Version = true; break;
                case "--output": case "-o": result.Output = Value(); break;
                case "--name":
                    result.Name = Value().Trim();
                    if (result.Name.Length == 0) throw new ArgumentException("Report name cannot be empty.");
                    break;
                case "--json": result.Json = Value(); break;
                case "--assets-root": result.AssetsRoot = Value(); break;
                case "--restore": result.Restore = true; break;
                case "--open": result.Open = true; break;
                case "--demo": result.Demo = true; break;
                case "--fail-on-drift": result.FailOnDrift = true; break;
                case "--fail-on-incomplete": result.FailOnIncomplete = true; break;
                default:
                    if (args[i].StartsWith('-')) throw new ArgumentException($"Unknown option: {args[i]}. Use --help.");
                    if (hasInput) throw new ArgumentException("Specify a single input path.");
                    result.Input = args[i]; hasInput = true; break;
            }
        }
        return result;
    }

    public const string HelpText = """
        nuget-map — explore your NuGet dependency landscape

        Usage: dotnet nuget-map [solution | project | folder] [options]

        -o, --output <file.html>   Standalone report (default: nuget-map.html)
        --name <report name>      Report title; prompted for in interactive terminals
        --json <file.json>        Also export the graph as JSON
        --restore                Run dotnet restore before analysis
        --assets-root <folder>   Find custom project.assets.json paths by project identity
        --open                   Open the report in your default browser
        --fail-on-drift           Exit 2 when resolved package versions differ
        --fail-on-incomplete      Exit 3 on incomplete data or restore diagnostics
        --demo                   Generate a clearly marked, illustrative sample
        -h, --help               Show help
        --version                Show version

        Reads existing restore graphs by default. No network calls or MSBuild execution
        unless --restore is requested. Exit codes: 0 success, 1 error, 2 drift,
        3 incomplete (takes precedence over drift). Reports are still written for 2/3.
        Non-interactive runs require --name. The report title does not change the
        output filename; use --output to choose that separately.
        """;
}

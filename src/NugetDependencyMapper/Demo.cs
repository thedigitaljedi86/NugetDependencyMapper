namespace NugetDependencyMapper;

public static class Demo
{
    public static DependencyReport Create()
    {
        var report = new DependencyReport { Name = "Northstar Commerce", IsDemo = true };
        Add("Storefront.Api", "services/Storefront.Api", [("Serilog.AspNetCore", "8.0.1"), ("MediatR", "12.2.0"), ("FluentValidation", "11.9.0")], "8.0.0", "13.0.3");
        Add("Orders.Worker", "services/Orders.Worker", [("MassTransit", "8.2.0"), ("Serilog.AspNetCore", "7.0.0")], "7.0.0", "13.0.1");
        Add("Catalog.Infrastructure", "src/Catalog.Infrastructure", [("Microsoft.EntityFrameworkCore", "8.0.4"), ("Npgsql", "8.0.3")], "8.0.0", "13.0.3");
        Add("Payments.Api", "services/Payments.Api", [("Serilog.AspNetCore", "8.0.1"), ("FluentValidation", "11.9.0"), ("Polly", "8.3.0")], "8.0.0", "13.0.3");
        Add("Commerce.Tests", "tests/Commerce.Tests", [("xunit", "2.7.0"), ("FluentAssertions", "6.12.0")], "8.0.0", "13.0.1");
        // Illustrative metadata, not authoritative information about these real packages.
        foreach (var graph in report.Projects.SelectMany(p => p.Targets))
            graph.Packages = graph.Packages.Select(p => p with
            {
                License = p.Id switch
                {
                    "MassTransit" => LicenseInfo.Unknown("Illustrative example of missing license metadata."),
                    "FluentAssertions" => new("file", "LICENSE.txt", RequireAcceptance: true, Note: "Illustrative license file; review the actual package for its terms."),
                    _ => new("expression", "MIT", "https://licenses.nuget.org/MIT", Note: "Illustrative demo metadata.")
                }
            }).ToList();
        ReportIndex.Build(report);
        return report;

        void Add(string name, string path, (string Id, string Version)[] roots, string logging, string json)
        {
            var project = new ProjectInfo { Id = path + "/" + name + ".csproj", Name = name, Path = path + "/" + name + ".csproj", Resolved = true };
            var graph = new TargetGraph { Name = "net8.0" };
            foreach (var (id, version) in roots)
            {
                graph.Packages.Add(new($"{id}/{version}", id, version, true, $"[{version}, )", true));
                graph.Roots.Add($"{id}/{version}");
            }
            graph.Packages.Add(new($"Microsoft.Extensions.Logging/{logging}", "Microsoft.Extensions.Logging", logging, false, null, true));
            graph.Packages.Add(new($"Microsoft.Extensions.DependencyInjection/{logging}", "Microsoft.Extensions.DependencyInjection", logging, false, null, true));
            graph.Packages.Add(new($"Newtonsoft.Json/{json}", "Newtonsoft.Json", json, false, null, true));
            graph.Edges.Add(new(graph.Roots[0], $"Microsoft.Extensions.Logging/{logging}", $">= {logging}"));
            graph.Edges.Add(new($"Microsoft.Extensions.Logging/{logging}", $"Microsoft.Extensions.DependencyInjection/{logging}", $">= {logging}"));
            graph.Edges.Add(new(graph.Roots[^1], $"Newtonsoft.Json/{json}", $">= {json}"));
            project.Targets.Add(graph);
            report.Projects.Add(project);
        }
    }
}

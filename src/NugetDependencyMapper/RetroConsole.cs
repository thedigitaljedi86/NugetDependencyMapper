using System.Text;

namespace NugetDependencyMapper;

internal static class RetroConsole
{
    public static bool Enabled { get; } = !Console.IsOutputRedirected && Environment.GetEnvironmentVariable("NO_COLOR") is null;

    static RetroConsole()
    {
        if (Enabled) { try { Console.OutputEncoding = Encoding.UTF8; } catch (IOException) { } }
    }

    public static void Banner()
    {
        if (!Enabled) return;
        DrawBox(null, "NuGet Dependency Mapper", "— Setup Wizard —");
    }

    public static void Step(string projectPath, int current, int total)
    {
        if (!Enabled) { Console.WriteLine($"Restoring {projectPath}..."); return; }
        const int barWidth = 24;
        var filled = total <= 0 ? barWidth : Math.Clamp((int)Math.Round(barWidth * (double)current / total), 0, barWidth);
        var bar = new string('█', filled) + new string('░', barWidth - filled);
        var pct = total <= 0 ? 100 : Math.Clamp((int)Math.Round(100.0 * current / total), 0, 100);
        WithColors(() => Console.WriteLine($"  [{bar}] {pct,3}%  Restoring {Path.GetFileName(projectPath)}"));
    }

    public static void Success(string path) => DrawBox(ConsoleColor.DarkGreen, "✓  All done!", $"Report generated: {path}");

    public static void Failure(string message) => DrawBox(ConsoleColor.DarkRed, "☹  Setup did not complete", message);

    private static void DrawBox(ConsoleColor? accent, params string[] lines)
    {
        if (!Enabled) { foreach (var line in lines) Console.WriteLine(line); return; }
        const int maxWidth = 74;
        var width = Math.Clamp(lines.Max(l => l.Length) + 4, 30, maxWidth);
        WithColors(() =>
        {
            Console.WriteLine("╔" + new string('═', width - 2) + "╗");
            for (var i = 0; i < lines.Length; i++)
                WriteCentered(Truncate(lines[i], width - 4), width, i == 0 ? accent : null);
            Console.WriteLine("╚" + new string('═', width - 2) + "╝");
        });
        Console.WriteLine();
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..Math.Max(0, max - 1)] + "…";

    private static void WriteCentered(string text, int width, ConsoleColor? fg)
    {
        var pad = width - 2 - text.Length;
        var left = pad / 2;
        var right = pad - left;
        var line = "║" + new string(' ', left) + text + new string(' ', right) + "║";
        if (fg is { } color)
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(line);
            Console.ForegroundColor = previous;
        }
        else Console.WriteLine(line);
    }

    private static void WithColors(Action action)
    {
        var background = Console.BackgroundColor;
        var foreground = Console.ForegroundColor;
        try
        {
            Console.BackgroundColor = ConsoleColor.Gray;
            Console.ForegroundColor = ConsoleColor.Black;
            action();
        }
        finally
        {
            Console.BackgroundColor = background;
            Console.ForegroundColor = foreground;
        }
    }
}

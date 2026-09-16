using System.Text;

namespace NugetDependencyMapper;

/// <summary>
/// Full-screen "text-mode setup" chrome styled after the classic Windows XP installer:
/// a blue screen with a title bar, a status bar branded "Powered by IT Performance",
/// and a content area that is redrawn in place as the tool works.
/// </summary>
internal static class RetroConsole
{
    private const string EnterAltScreen = "\x1b[?1049h";
    private const string ExitAltScreen = "\x1b[?1049l";
    private const string Footer = "Powered by IT Performance";
    private const int HeaderRows = 3;
    private const int FooterRows = 1;

    /// <summary>
    /// The chrome is purely decorative and takes over the terminal, so it only runs when both
    /// streams are a real terminal. NO_COLOR and NUGET_MAP_PLAIN opt out explicitly.
    /// </summary>
    public static bool Enabled { get; } = !Console.IsOutputRedirected && !Console.IsInputRedirected
        && Environment.GetEnvironmentVariable("NO_COLOR") is null
        && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NUGET_MAP_PLAIN"));

    private static bool _active;
    private static int _width;
    private static int _height;

    public static void Begin(string title)
    {
        if (!Enabled) return;
        try
        {
            _width = Math.Clamp(Console.WindowWidth, 40, 200);
            _height = Math.Clamp(Console.WindowHeight, 12, 60);
            Console.Out.Write(EnterAltScreen);
            Console.CursorVisible = false;
            Console.BackgroundColor = ConsoleColor.Blue;
            Console.ForegroundColor = ConsoleColor.White;
            for (var row = 0; row < _height; row++)
            {
                Console.SetCursorPosition(0, row);
                Console.Write(new string(' ', _width));
            }
            Console.SetCursorPosition(0, 0);
            Console.Write(Fit($"  {title}", _width));
            Console.SetCursorPosition(0, 1);
            Console.Write(new string('─', _width));
            WriteFooter();
            _active = true;
            Console.CancelKeyPress += RestoreTerminalOnCancel;
        }
        catch { _active = false; }
    }

    public static void Welcome(string headline, params string[] lines)
    {
        if (!_active) return;
        try
        {
            var content = new List<string> { headline, string.Empty };
            content.AddRange(lines);
            DrawContent(content);
        }
        catch { _active = false; }
    }

    public static void Progress(string projectPath, int current, int total)
    {
        if (!_active) { Console.WriteLine($"Restoring {projectPath}..."); return; }
        try
        {
            DrawContent(
            [
                "Restoring NuGet packages for your projects.",
                "This might take a few minutes.",
                string.Empty,
                $"Restoring:  {Path.GetFileName(projectPath)}  ({current} of {total})",
                string.Empty,
                $"[{Bar(current, total)}] {Percent(current, total),3}%",
            ]);
        }
        catch { _active = false; }
    }

    public static void Scanning(string projectPath, int current, int total)
    {
        if (!_active) return;
        try
        {
            DrawContent(
            [
                "Mapping projects and packages.",
                "This might take a moment for large solutions.",
                string.Empty,
                $"Scanning:  {Path.GetFileName(projectPath)}  ({current} of {total})",
                string.Empty,
                $"[{Bar(current, total)}] {Percent(current, total),3}%",
            ]);
        }
        catch { _active = false; }
    }

    /// <summary>
    /// Reads the report name as its own screen inside the retro UI. Only called when input is
    /// interactive, so a blocked read here always means a rendering failure, not missing input.
    /// </summary>
    public static string? PromptReportName()
    {
        if (!_active) return null;
        try
        {
            DrawContent(
            [
                "Name this dependency report.",
                "This title appears at the top of the generated HTML report.",
                string.Empty,
                "Report name:",
            ]);
            var row = HeaderRows + 4;
            Console.SetCursorPosition(2, row);
            Console.Write("> ");
            Console.CursorVisible = true;
            var name = new StringBuilder();
            var maxLength = Math.Max(1, _width - 8);
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                {
                    if (name.Length > 0) break;
                    continue;
                }
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (name.Length == 0) continue;
                    name.Length--;
                    Console.SetCursorPosition(4 + name.Length, row);
                    Console.Write(' ');
                    Console.SetCursorPosition(4 + name.Length, row);
                    continue;
                }
                if (!char.IsControl(key.KeyChar) && name.Length < maxLength)
                {
                    name.Append(key.KeyChar);
                    Console.Write(key.KeyChar);
                }
            }
            Console.CursorVisible = false;
            return name.ToString().Trim();
        }
        catch { _active = false; return null; }
    }

    private static string Bar(int current, int total)
    {
        const int barWidth = 40;
        var filled = total <= 0 ? barWidth : Math.Clamp((int)Math.Round(barWidth * (double)current / total), 0, barWidth);
        return new string('█', filled) + new string('░', barWidth - filled);
    }

    private static int Percent(int current, int total)
        => total <= 0 ? 100 : Math.Clamp((int)Math.Round(100.0 * current / total), 0, 100);

    public static void Finish(bool success, string headline, params string[] detailLines)
    {
        if (!_active) return;
        try
        {
            var content = new List<string> { headline, string.Empty };
            content.AddRange(detailLines);
            content.Add(string.Empty);
            content.Add("Press ENTER to exit.");
            DrawContent(content, success ? ConsoleColor.Green : ConsoleColor.Red);
            if (!Console.IsInputRedirected)
                while (Console.ReadKey(intercept: true).Key != ConsoleKey.Enter) { }
        }
        catch { /* purely decorative; never fail the run over a rendering hiccup */ }
        finally
        {
            Console.CancelKeyPress -= RestoreTerminalOnCancel;
            try { Console.CursorVisible = true; } catch { /* best effort */ }
            try { Console.Out.Write(ExitAltScreen); } catch { /* best effort */ }
            _active = false;
        }
    }

    private static void RestoreTerminalOnCancel(object? sender, ConsoleCancelEventArgs e)
    {
        if (!_active) return;
        _active = false;
        try { Console.CursorVisible = true; } catch { /* best effort */ }
        try { Console.Out.Write(ExitAltScreen); } catch { /* best effort */ }
    }

    private static void WriteFooter()
    {
        Console.BackgroundColor = ConsoleColor.Gray;
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.SetCursorPosition(0, _height - 1);
        Console.Write(Fit($" {Footer}", _width));
        Console.BackgroundColor = ConsoleColor.Blue;
        Console.ForegroundColor = ConsoleColor.White;
    }

    private static void DrawContent(IReadOnlyList<string> lines, ConsoleColor? headlineColor = null)
    {
        var top = HeaderRows;
        var bottom = _height - FooterRows;
        for (var row = top; row < bottom; row++)
        {
            var i = row - top;
            Console.SetCursorPosition(0, row);
            Console.ForegroundColor = i == 0 && headlineColor is { } color ? color : ConsoleColor.White;
            Console.Write(i < lines.Count ? Fit("  " + lines[i], _width) : new string(' ', _width));
        }
        Console.ForegroundColor = ConsoleColor.White;
    }

    private static string Fit(string text, int width)
        => text.Length >= width ? text[..Math.Max(0, width - 1)] : text.PadRight(width);
}

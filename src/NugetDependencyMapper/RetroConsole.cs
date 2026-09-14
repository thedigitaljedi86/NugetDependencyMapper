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

    public static bool Enabled { get; } = !Console.IsOutputRedirected && Environment.GetEnvironmentVariable("NO_COLOR") is null;

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
            const int barWidth = 40;
            var filled = total <= 0 ? barWidth : Math.Clamp((int)Math.Round(barWidth * (double)current / total), 0, barWidth);
            var bar = new string('█', filled) + new string('░', barWidth - filled);
            var pct = total <= 0 ? 100 : Math.Clamp((int)Math.Round(100.0 * current / total), 0, 100);
            DrawContent(
            [
                "Setup is restoring NuGet packages for your projects.",
                "This might take a few minutes.",
                string.Empty,
                $"Restoring:  {Path.GetFileName(projectPath)}  ({current} of {total})",
                string.Empty,
                $"[{bar}] {pct,3}%",
            ]);
        }
        catch { _active = false; }
    }

    public static async Task Finish(bool success, string headline, params string[] detailLines)
    {
        if (!_active) return;
        try
        {
            var content = new List<string> { headline, string.Empty };
            content.AddRange(detailLines);
            DrawContent(content, success ? ConsoleColor.Green : ConsoleColor.Red);
            await Task.Delay(1100);
        }
        catch { /* purely decorative; never fail the run over a rendering hiccup */ }
        finally
        {
            try { Console.CursorVisible = true; } catch { /* best effort */ }
            try { Console.Out.Write(ExitAltScreen); } catch { /* best effort */ }
            _active = false;
        }
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

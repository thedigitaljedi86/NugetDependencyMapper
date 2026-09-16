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
    // Below this the chrome cannot be drawn without wrapping, which scrolls the title and
    // the status bar off screen. A smaller window gets plain line output instead.
    private const int MinWidth = 40;
    private const int MinHeight = 12;
    // ConsoleColor.Blue is the *bright* blue (SGR 104). The text-mode setup this imitates used
    // the classic VGA dark blue, index 1, which .NET calls DarkBlue (SGR 44).
    private const ConsoleColor Background = ConsoleColor.DarkBlue;

    /// <summary>
    /// The chrome is purely decorative and takes over the terminal, so it only runs when both
    /// streams are a real terminal. NO_COLOR and NUGET_MAP_PLAIN opt out explicitly.
    /// </summary>
    public static bool Enabled { get; } = !Console.IsOutputRedirected && !Console.IsInputRedirected
        && Environment.GetEnvironmentVariable("NO_COLOR") is null
        && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NUGET_MAP_PLAIN"));

    private static bool _active;
    private static bool _entered;
    private static string _title = "";
    private static int _width;
    private static int _height;
    private static int _observedWidth;
    private static int _observedHeight;

    public static void Begin(string title)
    {
        if (!Enabled) return;
        try
        {
            _title = title;
            // Measured before anything is written, so a window too small for the chrome
            // leaves the terminal completely untouched.
            if (!Measure()) return;
            Console.Out.Write(EnterAltScreen);
            _entered = true;
            Console.CursorVisible = false;
            Paint();
            _active = true;
            Console.CancelKeyPress += RestoreTerminalOnCancel;
        }
        catch { Restore(); }
    }

    /// <summary>
    /// Caches the drawable area. Never wider or taller than the real window: longer lines wrap
    /// and push the header and footer out of view.
    /// </summary>
    private static bool Measure()
    {
        var width = Console.WindowWidth;
        var height = Console.WindowHeight;
        if (!SupportsChrome(width, height)) return false;
        _observedWidth = width;
        _observedHeight = height;
        _width = Math.Min(width, 200);
        _height = Math.Min(height, 60);
        return true;
    }

    /// <summary>Follows a window resize, and stands down if the window became too small.</summary>
    private static bool Resync()
    {
        if (!_active) return false;
        if (Console.WindowWidth == _observedWidth && Console.WindowHeight == _observedHeight) return true;
        if (!Measure()) { Restore(); return false; }
        Paint();
        return true;
    }

    private static void Paint()
    {
        Console.BackgroundColor = Background;
        Console.ForegroundColor = ConsoleColor.White;
        for (var row = 0; row < _height; row++)
        {
            Console.SetCursorPosition(0, row);
            Console.Write(new string(' ', _width));
        }
        Console.SetCursorPosition(0, 0);
        Console.Write(Fit($"  {_title}", _width));
        Console.SetCursorPosition(0, 1);
        Console.Write(new string('─', _width));
        WriteFooter();
    }

    /// <summary>Hands the terminal back. Safe to call more than once, and when nothing was drawn.</summary>
    private static void Restore()
    {
        _active = false;
        Console.CancelKeyPress -= RestoreTerminalOnCancel;
        if (!_entered) return;
        _entered = false;
        try { Console.ResetColor(); } catch { /* best effort */ }
        try { Console.CursorVisible = true; } catch { /* best effort */ }
        try { Console.Out.Write(ExitAltScreen); } catch { /* best effort */ }
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
        catch { Restore(); }
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
                Line("Restoring:", projectPath, $"({current} of {total})", _width),
                string.Empty,
                $"[{Bar(current, total, _width)}] {Percent(current, total),3}%",
            ]);
        }
        catch { Restore(); }
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
                Line("Scanning:", projectPath, $"({current} of {total})", _width),
                string.Empty,
                $"[{Bar(current, total, _width)}] {Percent(current, total),3}%",
            ]);
        }
        catch { Restore(); }
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
        catch { Restore(); return null; }
    }

    /// <summary>Whether the window can hold the chrome at all.</summary>
    internal static bool SupportsChrome(int width, int height) => width >= MinWidth && height >= MinHeight;

    /// <summary>Keeps the trailing counter visible by shortening the file name instead.</summary>
    internal static string Line(string label, string projectPath, string suffix, int width)
    {
        var name = Path.GetFileName(projectPath);
        var room = width - label.Length - suffix.Length - 7;
        if (name.Length > room) name = room > 1 ? name[..(room - 1)] + "…" : "";
        return $"{label}  {name}  {suffix}";
    }

    internal static string Bar(int current, int total, int width)
    {
        // "  [bar] 100%" has to fit the content width, which Fit would otherwise clip.
        var barWidth = Math.Clamp(width - 12, 10, 40);
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
            Restore();
        }
    }

    private static void RestoreTerminalOnCancel(object? sender, ConsoleCancelEventArgs e) => Restore();

    private static void WriteFooter()
    {
        Console.BackgroundColor = ConsoleColor.Gray;
        Console.ForegroundColor = Background;
        Console.SetCursorPosition(0, _height - 1);
        Console.Write(Fit($" {Footer}", _width));
        Console.BackgroundColor = Background;
        Console.ForegroundColor = ConsoleColor.White;
    }

    private static void DrawContent(IReadOnlyList<string> lines, ConsoleColor? headlineColor = null)
    {
        if (!Resync()) return;
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

    internal static string Fit(string text, int width)
        => text.Length >= width ? text[..Math.Max(0, width - 1)] : text.PadRight(width);
}

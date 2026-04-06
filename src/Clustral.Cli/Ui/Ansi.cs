namespace Clustral.Cli.Ui;

/// <summary>
/// ANSI escape code helpers for colorized CLI output.
/// Automatically disabled when stdout is not a terminal (piped/redirected).
/// </summary>
internal static class Ansi
{
    private static readonly bool _enabled = !Console.IsOutputRedirected;

    // Colors
    public static string Green(string s)   => Wrap(s, "32");
    public static string Yellow(string s)  => Wrap(s, "33");
    public static string Red(string s)     => Wrap(s, "31");
    public static string Cyan(string s)    => Wrap(s, "36");
    public static string Blue(string s)    => Wrap(s, "34");
    public static string Magenta(string s) => Wrap(s, "35");
    public static string Gray(string s)    => Wrap(s, "90");
    public static string Bold(string s)    => Wrap(s, "1");
    public static string Dim(string s)     => Wrap(s, "2");

    // Symbols
    public const string Check    = "✓";
    public const string Cross    = "✗";
    public const string Arrow    = "→";
    public const string Dot      = "●";
    public const string Diamond  = "◆";
    public const string Pointer  = "▸";
    public const string Shield   = "🛡";
    public const string Key      = "🔑";
    public const string Globe    = "🌐";
    public const string Lock     = "🔒";
    public const string Cluster  = "☸";
    public const string User     = "👤";
    public const string Clock    = "⏱";

    /// <summary>
    /// Pads a string to <paramref name="width"/> based on visible length,
    /// ignoring ANSI escape codes that throw off column alignment.
    /// </summary>
    public static string Pad(string s, int width)
    {
        var visible = System.Text.RegularExpressions.Regex.Replace(s, @"\x1b\[[0-9;]*m", "");
        return s + new string(' ', Math.Max(0, width - visible.Length));
    }

    private static string Wrap(string s, string code) =>
        _enabled ? $"\x1b[{code}m{s}\x1b[0m" : s;
}

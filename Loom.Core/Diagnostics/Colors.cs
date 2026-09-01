namespace Loom.Core.Diagnostics;

/// <summary>
///     ANSI escape codes for console output, blanked out rather than emitted whenever the output isn't a
///     terminal that would render them - a redirected build log or a CI runner otherwise fills up with raw
///     escape sequences instead of color, and <c>NO_COLOR</c> is the convention for opting out by hand.
/// </summary>
public static class Colors
{
    private static readonly bool _enabled =
        Environment.GetEnvironmentVariable("NO_COLOR") is not { Length: > 0 } && !Console.IsOutputRedirected && !Console.IsErrorRedirected;

    public static string Reset => Code("\e[0m");
    public static string Bold => Code("\e[1m");
    public static string Dim => Code("\e[2m");

    public static string Red => Code("\e[38;5;9m");
    public static string Yellow => Code("\e[38;5;11m");
    public static string Orange => Code("\e[38;5;3m");
    public static string Green => Code("\e[38;5;2m");
    public static string Blue => Code("\e[38;5;12m");
    public static string Cyan => Code("\e[38;5;38m");
    public static string Magenta => Code("\e[38;5;135m");
    public static string Pink => Code("\e[38;5;219m");
    public static string White => Code("\e[38;5;231m");
    public static string Gray => Code("\e[38;5;252m");

    private static string Code(string code) => _enabled ? code : "";
}
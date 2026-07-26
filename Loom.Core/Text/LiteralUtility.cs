using System.Globalization;

namespace Loom.Core.Text;

public static class LiteralUtility
{
    public static object? ResolveValue(Token token) =>
        token.Kind switch
        {
            SyntaxKind.NumberLiteral => ResolveNumber(token),

            // synthesized tokens from failed parses carry no quotes to strip
            SyntaxKind.StringLiteral => token.Text.Length < 2 ? null : token.Text[1..^1],
            SyntaxKind.TrueLiteral => true,
            SyntaxKind.FalseLiteral => false,
            _ => null
        };

#pragma warning disable CA1859
    private static object ResolveNumber(Token token)
#pragma warning restore CA1859
    {
        var value = ParseNumberValue(token);
        const double epsilon = 2.220446049250313e-16;
        if (Math.Abs(Math.Floor(value) - value) < epsilon)
            return (long)value;

        return value;
    }

    private static double ParseNumberValue(Token token)
    {
        var text = token.Text.Replace("_", "");
        return text switch
        {
            _ when text.EndsWith("hz", StringComparison.OrdinalIgnoreCase) => 1 / double.Parse(text[..^2]),
            _ when text.EndsWith("ms", StringComparison.OrdinalIgnoreCase) => double.Parse(text[..^2]) / 1000,
            _ when text.EndsWith("s", StringComparison.OrdinalIgnoreCase) => double.Parse(text[..^1]),
            _ when text.EndsWith("m", StringComparison.OrdinalIgnoreCase) => 60 * double.Parse(text[..^1]),
            _ when text.EndsWith("h", StringComparison.OrdinalIgnoreCase) => 3600 * double.Parse(text[..^1]),
            _ when text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) => long.Parse(text[2..], NumberStyles.HexNumber),
            _ when text.StartsWith("0b", StringComparison.OrdinalIgnoreCase) => long.Parse(text[2..], NumberStyles.BinaryNumber),
            _ when text.StartsWith("0o", StringComparison.OrdinalIgnoreCase) => Convert.ToInt64(text[2..], 8),
            _ => double.Parse(text)
        };
    }
}
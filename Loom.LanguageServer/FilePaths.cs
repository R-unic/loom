namespace Loom.LanguageServer;

/// <summary>
///     Compares the paths a request names against the paths the compiler holds. The two are not written the
///     same way even when they are the same file: a client's <c>file:</c> URI round-trips a Windows drive
///     letter in lower case, while the compiler's path came from the project directory in whatever case the
///     configuration used - so an ordinal comparison drops the match and the request answers about nothing.
/// </summary>
public static class FilePaths
{
    public static readonly StringComparer Comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static bool Same(string? left, string? right) => Comparer.Equals(left, right);
}

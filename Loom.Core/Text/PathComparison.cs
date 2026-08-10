namespace Loom.Core.Text;

/// <summary>
///     How the compiler compares file paths: the way the filesystem underneath does. Two paths naming one file
///     are routinely written differently - a language client's <c>file:</c> URI round-trips a Windows drive
///     letter in lower case, and a configured directory keeps whatever case it was written in - so comparing
///     them ordinally answers "different file" for the same file, and every decision keyed on identity goes
///     with it.
/// </summary>
public static class PathComparison
{
    public static readonly StringComparer Comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    public static readonly StringComparison Comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static bool Same(string? left, string? right) => Comparer.Equals(left, right);

    /// <summary>Whether <paramref name="path" /> names <paramref name="directory" /> itself or something inside it.</summary>
    public static bool IsUnder(string path, string directory) =>
        Same(path, directory) || path.StartsWith(directory + Path.DirectorySeparatorChar, Comparison);
}

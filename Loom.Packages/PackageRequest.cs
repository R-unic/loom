using System.Diagnostics.CodeAnalysis;
using Loom.Config;

namespace Loom.Packages;

/// <summary>
///     One package a caller asked to depend on, as it was asked for: a name, the versions it will accept when it
///     named any, and whether it is wanted only to develop the project.
/// </summary>
/// <remarks>
///     A request with no requirement is not the same as one asking for <c>*</c>: it is a caller with no opinion,
///     which <see cref="PackageAdder" /> answers by writing down what is published now. Nothing else can answer it,
///     since only an index knows what that is.
/// </remarks>
/// <param name="Requirement">The versions accepted, or <see langword="null" /> when the request named none.</param>
public sealed record PackageRequest(PackageName Name, VersionRequirement? Requirement, bool IsDevelopmentOnly = false)
{
    private const char RequirementSeparator = '@';

    /// <summary>
    ///     Reads a request written as <c>name</c>, <c>scope/name</c> or either followed by <c>@requirement</c> — the
    ///     form a command line takes it in. A bare version (<c>math@1.2.3</c>) is a requirement like any other, and
    ///     means what it means in a manifest.
    /// </summary>
    public static bool TryParse(
        string? text,
        bool isDevelopmentOnly,
        [NotNullWhen(true)] out PackageRequest? request,
        [NotNullWhen(false)] out string? error
    )
    {
        request = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "expected a package to add, e.g. 'math' or 'math@^1.2'.";
            return false;
        }

        var trimmed = text.Trim();
        var separator = trimmed.IndexOf(RequirementSeparator);
        var name = separator < 0 ? trimmed : trimmed[..separator];
        var requirementText = separator < 0 ? null : trimmed[(separator + 1)..].Trim();
        if (!PackageName.TryParse(name, out var packageName, out error))
            return false;

        if (requirementText == null)
        {
            request = new PackageRequest(packageName, null, isDevelopmentOnly);
            error = null;
            return true;
        }

        if (!VersionRequirement.TryParse(requirementText, out var requirement, out error))
            return false;

        request = new PackageRequest(packageName, requirement, isDevelopmentOnly);
        error = null;
        return true;
    }

    public override string ToString() => Requirement == null ? Name.ToString() : $"{Name}{RequirementSeparator}{Requirement}";
}

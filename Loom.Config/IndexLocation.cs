namespace Loom.Config;

/// <summary>
///     Whether a string names somewhere a package index could be. Asked in two places that must agree: the
///     <c>[registry] index</c> a project resolves from, and the <c>source</c> a lock file records a version as
///     having come from — the second is written from the first, so a rule they disagreed on would reject locks
///     the manifest that produced them allows.
/// </summary>
/// <remarks>
///     A registry is reached over http, but an index is just as legitimately a directory — vendored, checked out
///     beside the project, or the fixtures a test resolves against — so this cannot ask for a URL. What is left to
///     check is that the text could name a place at all, which catches the empty and the unusable and nothing else.
/// </remarks>
internal static class IndexLocation
{
    internal const string Expected = "expected an http or https URL, or a path to a local index";

    internal static bool IsValid(string? location) =>
        !string.IsNullOrWhiteSpace(location) && location.IndexOfAny(Path.GetInvalidPathChars()) < 0;
}

using Loom.Core.Modules;
using Loom.Core.Pipeline;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;

namespace Loom.LanguageServer;

/// <summary>
///     Where a name in scope came from. A file's scope is fed from four places that look identical at the use
///     site - the file itself, an import, the project's <c>.d.loom</c> files, and the language's intrinsics -
///     and which one a name arrived through decides whether it can be changed, and where to go looking for it.
/// </summary>
public static class SymbolOrigin
{
    /// <summary>A one-line description of <paramref name="symbol" />'s source, or null when it is declared in <paramref name="viewedFrom" /> itself.</summary>
    public static string? Describe(Symbol symbol, SourceFile viewedFrom, CompilationUnit unit)
    {
        if (symbol.IsIntrinsic)
            return "intrinsic";

        if (FilePaths.Same(symbol.File.AbsolutePath, viewedFrom.AbsolutePath))
            return null;

        if (PackageOf(symbol.File, viewedFrom, unit) is { } package)
            return $"package `{package}`";

        if (symbol.File.IsDeclaration)
            return $"ambient, declared in `{symbol.File.Name}`";

        return SpecifierOf(symbol.File, viewedFrom, unit) is { } specifier ? $"exported by `{specifier}`" : $"declared in `{symbol.File.Name}`";
    }

    private static string? PackageOf(SourceFile declaringFile, SourceFile viewedFrom, CompilationUnit unit)
    {
        var root = RootOf(declaringFile, unit);
        return root == null || root == RootOf(viewedFrom, unit) ? null : root.Package?.Name?.ToString();
    }

    private static string? SpecifierOf(SourceFile declaringFile, SourceFile viewedFrom, CompilationUnit unit)
    {
        try
        {
            var specifier = new ModuleResolver(unit.SourceFiles, unit.Roots).SpecifierOf(viewedFrom, declaringFile);
            return string.IsNullOrEmpty(specifier) ? null : specifier;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static SourceRoot? RootOf(SourceFile file, CompilationUnit unit)
    {
        try
        {
            return unit.Roots.Of(file);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

using Loom.Core.Lexing;
using Loom.Core.Parsing;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;

namespace Loom.Core;

/// <summary>
///     A source file that has been lexed and parsed but not yet analyzed.
///     <see cref="CompilationUnit" /> produces one for every file in the unit before analyzing any of
///     them, so module dependencies can be resolved and ordered while every tree is already available.
/// </summary>
public sealed record ParsedFile(SourceFile File, LexerResult LexerResult, ParserResult ParserResult)
{
    public Tree Tree => ParserResult.Tree;

    /// <summary>Top-level imports, in source order. Imports nested in a block are rejected by the resolver.</summary>
    public IReadOnlyList<ImportDeclaration> Imports => field ??= Tree.Statements.OfType<ImportDeclaration>().ToArray();

    public IReadOnlyList<NamespaceImport> NamespaceImports => field ??= Tree.Statements.OfType<NamespaceImport>().ToArray();

    public IReadOnlyList<ExportList> ReExports => field ??= Tree.Statements.OfType<ExportList>().Where(export => export.IsReExport).ToArray();
}
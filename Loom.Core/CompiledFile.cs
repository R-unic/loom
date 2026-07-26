using System.Text;
using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.Text;
using Loom.Luau.AST;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core;

public sealed class CompiledFile(SourceFile sourceFile)
{
    public SourceFile SourceFile { get; } = sourceFile;
    public required string Path { get; init; }
    public required DiagnosticBag Diagnostics { get; init; }
    public required string RenderedLuau { get; init; }
    public required LuauTree LuauTree { get; init; }
    public required Type ReturnType { get; init; }
    public required SemanticModel SemanticModel { get; init; }
    public required Tree Tree { get; init; }
    public required IReadOnlyList<Token> Tokens { get; init; }

    public string GetDebugInfo(
        bool rebuilt = true,
        bool luau = true,
        bool showDiagnostics = true,
        bool debugDiagnostics = true)
    {
        var sb = new StringBuilder();

        if (rebuilt)
        {
            appendHeader("Rebuilt program");
            sb.AppendLine(Tree.ToString());
            sb.AppendLine();
        }

        if (luau)
        {
            appendHeader("Compiled Luau program");
            sb.AppendLine(RenderedLuau);
            sb.AppendLine();
        }

        if (showDiagnostics)
        {
            appendHeader("Diagnostics");
            var compilerDiagnostics = (debugDiagnostics ? Diagnostics : Diagnostics.WithoutInfo()).ToString();
            sb.AppendLine(string.IsNullOrEmpty(compilerDiagnostics) ? "(none)" : compilerDiagnostics);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();

        void appendHeader(string title)
        {
            var line = new string('─', Math.Min(title.Length + 12, 60));
            sb.AppendLine(line);
            sb.AppendLine($"  {title}");
            sb.AppendLine(line);
        }
    }
}
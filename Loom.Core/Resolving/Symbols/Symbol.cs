using Loom.Core.Parsing.AST;
using Loom.Core.Text;

namespace Loom.Core.Resolving.Symbols;

public class Symbol(Node declaration, SymbolKind kind, string name, bool isMutable = false)
{
    public SourceFile File { get; } = declaration.File;
    public Node Declaration { get; } = declaration;
    public SymbolKind Kind { get; } = kind;
    public string Name { get; } = name;
    public bool IsMutable { get; } = isMutable;
    public bool IsAmbient { get; internal set; }
    public bool IsIntrinsic { get; internal set; }
    public bool IsGlobal { get; internal set; }
    public bool IsTypeSymbol { get; } = IsTypeKind(kind);
    public bool IsValueSymbol { get; } = IsValueKind(kind);

    public bool EmitsRuntimeBinding => Declaration is VariableDeclaration or FunctionDeclaration;

    internal static bool IsTypeKind(SymbolKind kind) => kind is SymbolKind.Interface or SymbolKind.Type or SymbolKind.EnumType or SymbolKind.Trait;

    private static bool IsValueKind(SymbolKind kind) =>
        kind is SymbolKind.Variable or SymbolKind.Property or SymbolKind.Event or SymbolKind.InjectedPropertyVariable or SymbolKind.Function or SymbolKind.Parameter;

    public override string ToString() => $"Symbol({Kind}, {Name})";
}